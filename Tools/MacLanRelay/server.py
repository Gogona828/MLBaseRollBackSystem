#!/usr/bin/env python3
"""Two-player IPv4 UDP relay. Even base port = P1, base+1 = P2."""
import argparse
import asyncio
import logging
import json
import math
import random
import socket
import struct
import time

PACKET = struct.Struct('<BiiBi')
DEFAULT_INTERVAL_MIN_SECONDS = 8.0
DEFAULT_INTERVAL_MAX_SECONDS = 10.0
ROUND_START_LEAD_SECONDS = 1.0

class DelayEvents:
    """Shared, traffic-driven stalls. Intervals are measured start to start."""
    def __init__(self, delay, jitter, mode, interval_min, interval_max):
        self.delay, self.jitter, self.mode = delay, jitter, mode
        self.interval_min, self.interval_max = interval_min, interval_max
        self.next_event = None
        self.release_at = 0.0

    def wait_seconds(self, now):
        if self.mode == 'continuous':
            return max(0, self.delay + random.uniform(-self.jitter, self.jitter)) / 1000
        if self.next_event is None:
            self.next_event = now + random.uniform(self.interval_min, self.interval_max)
        if now >= self.next_event and now >= self.release_at:
            duration = max(0, self.delay + random.uniform(-self.jitter, self.jitter)) / 1000
            self.release_at = now + duration
            self.next_event = now + random.uniform(self.interval_min, self.interval_max)
            logging.info('Delay event: hold both input directions for %.1fms; next interval %.2fs',
                         duration*1000, self.next_event-now)
        return max(0, self.release_at-now)


class Relay:
    def __init__(self, delay=100, jitter=0, loss=0, timeout=15, mode='intermittent',
                 interval_min=DEFAULT_INTERVAL_MIN_SECONDS, interval_max=DEFAULT_INTERVAL_MAX_SECONDS):
        self.delay, self.jitter, self.loss, self.timeout = delay, jitter, loss, timeout
        self.peers = [None, None]
        self.seen = [0., 0.]
        self.transports = [None, None]
        self.generation = 0
        self.pending = set()
        self.delay_events = DelayEvents(delay, jitter, mode, interval_min, interval_max)
        self.delivery_at = [0., 0.]
        self.round_ready = {}
        self.round_starts = {}
        self.delay_revision = 0
        self.delay_active = False
        self.delay_until = 0.0
        self.delay_started_at = 0.0
        self.event_delay_ms = 0.0

    def receive(self, slot, data, address):
        if data.startswith(b'RRA1') and 4 < len(data) <= 4100:
            if self.peers[slot] == address and self.peers[1-slot]:
                self.seen[slot] = time.monotonic()
                self.receive_control(slot, data)
            return
        if len(data) != PACKET.size:
            return
        kind, player, frame, bits, start = PACKET.unpack(data)
        if kind not in (1, 2, 3, 4) or player != slot or bits > 7:
            return
        now = time.monotonic()
        if self.peers[slot] != address:
            if kind != 1 or (self.peers[slot] and now-self.seen[slot] < self.timeout):
                return
            self.generation += 1
            self.round_ready.clear()
            self.round_starts.clear()
            self.delay_events.next_event = None
            self.delay_events.release_at = 0
            self.delivery_at = [0., 0.]
            self.delay_active = False
            self.delay_until = 0
            self.delay_started_at = 0
            self.event_delay_ms = 0
            self.delay_revision += 1
            self.peers[slot] = address
            logging.info('P%s registered %s:%s', slot+1, *address)
        self.seen[slot] = now
        target = 1-slot
        if not self.peers[target] or now-self.seen[target] > self.timeout:
            return
        # Reliable control traffic is exempt from simulated loss. The host also
        # receives Start so neither client starts its countdown before the relay.
        targets = (0,1) if kind == 3 and slot == 0 else (target,)
        if kind == 3 and slot != 0:
            return
        input_delay = self.delay_events.wait_seconds(now) if kind == 4 else 0
        if kind == 4:
            self.update_delay_status(now, input_delay)
        for destination in targets:
            if kind == 4 and random.random()*100 < self.loss:
                continue
            delay = input_delay
            for repeat in range(3 if kind == 3 else 1):
                self.schedule(destination, data, delay+repeat*.05, ordered=kind == 4)

    def delay_status(self):
        return dict(delayRevision=self.delay_revision, delayActive=self.delay_active,
                    delayUntil=self.delay_until, delayMs=self.delay,
                    continuous=self.delay_events.mode == 'continuous',
                    delayStartedAt=self.delay_started_at, eventDelayMs=self.event_delay_ms,
                    delayJitterMs=self.jitter, lossPercent=self.loss,
                    intervalMinSeconds=self.delay_events.interval_min,
                    intervalMaxSeconds=self.delay_events.interval_max, delayProtocolVersion=2)

    def broadcast_delay_status(self):
        for slot in (0, 1):
            if self.peers[slot]:
                self.control_reply(slot, dict(kind='delayStatus', serverTime=time.monotonic(), **self.delay_status()))

    def update_delay_status(self, now, delay):
        if delay <= 0:
            return
        self.delay_until = max(self.delay_until, now+delay)
        if self.delay_active:
            return
        self.delay_active = True
        self.delay_started_at = now
        self.event_delay_ms = delay * 1000
        self.delay_revision += 1
        self.broadcast_delay_status()
        self.schedule_delay_end()

    def schedule_delay_end(self):
        generation = self.generation
        handle = None
        def expire():
            self.pending.discard(handle)
            if generation != self.generation:
                return
            if time.monotonic() < self.delay_until:
                self.schedule_delay_end()
                return
            self.delay_active = False
            self.delay_revision += 1
            self.broadcast_delay_status()
        handle = asyncio.get_running_loop().call_later(max(0, self.delay_until-time.monotonic()), expire)
        self.pending.add(handle)

    def receive_control(self, slot, data):
        try:
            message = json.loads(data[4:])
        except (ValueError, UnicodeError):
            return
        if not isinstance(message, dict):
            return
        kind = message.get('kind')
        if kind == 'clock':
            client_time = message.get('clientTime')
            if not isinstance(client_time, (int, float)) or not math.isfinite(client_time):
                return
            self.control_reply(slot, dict(kind='clock', clientTime=client_time, serverTime=time.monotonic(), **self.delay_status()))
        elif kind == 'roundReady':
            round_id, frame = message.get('round'), message.get('frame')
            if type(round_id) is not int or type(frame) is not int or not 1 <= round_id <= 10000 or not 0 <= frame < 2147483000:
                return
            # Both clients are frozen at the end of this round. A duplicate Ready
            # always returns the same deadline rather than restarting a countdown.
            if round_id not in self.round_starts:
                ready = self.round_ready.setdefault(round_id, {})
                ready[slot] = frame
                if len(ready) == 2:
                    self.round_starts[round_id] = dict(kind='roundStart', round=round_id,
                        nextFrame=max(ready.values())+1, startAt=time.monotonic()+ROUND_START_LEAD_SECONDS)
                    logging.info('Round %s barrier complete: nextFrame=%s, common start in %.1fs',
                                 round_id, self.round_starts[round_id]['nextFrame'], ROUND_START_LEAD_SECONDS)
            if round_id in self.round_starts:
                for destination in (0, 1):
                    self.control_reply(destination, self.round_starts[round_id])
            # Bound per-match bookkeeping even for malformed clients.
            for cache in (self.round_ready, self.round_starts):
                while len(cache) > 16:
                    del cache[min(cache)]
        else:
            self.schedule(1-slot, data, 0)

    def control_reply(self, slot, message):
        self.schedule(slot, b'RRA1'+json.dumps(message, separators=(',', ':')).encode(), 0)

    def schedule(self, slot, data, delay, ordered=False):
        if len(self.pending) >= 8192:
            return
        generation, address = self.generation, self.peers[slot]
        handle = None
        def send():
            self.pending.discard(handle)
            if generation == self.generation and address == self.peers[slot]:
                self.transports[slot].sendto(data,address)
        loop = asyncio.get_running_loop()
        delivery_at = loop.time()+delay
        if ordered:
            delivery_at = max(delivery_at, self.delivery_at[slot]+0.000001)
            self.delivery_at[slot] = delivery_at
        handle = loop.call_at(delivery_at,send)
        self.pending.add(handle)

class Port(asyncio.DatagramProtocol):
    def __init__(self, relay, slot): self.relay, self.slot = relay, slot
    def connection_made(self, transport): self.relay.transports[self.slot] = transport
    def datagram_received(self, data, address): self.relay.receive(self.slot,data,address)
    def error_received(self, error): logging.warning('UDP: %s', error)

async def serve(args):
    relay = Relay(args.delay,args.jitter,args.loss,mode=args.delay_mode,interval_min=args.interval_min,interval_max=args.interval_max)
    loop = asyncio.get_running_loop()
    try:
        for slot in (0,1):
            await loop.create_datagram_endpoint(lambda slot=slot: Port(relay,slot),local_addr=(args.bind,args.port+slot),family=socket.AF_INET)
        logging.info('Ready: P1 UDP %s / P2 UDP %s, mode=%s, delay=%sms +/- %sms, interval=%s..%ss, input loss=%s%%',args.port,args.port+1,args.delay_mode,args.delay,args.jitter,args.interval_min,args.interval_max,args.loss)
        await asyncio.Future()
    finally:
        for handle in relay.pending: handle.cancel()
        for transport in relay.transports:
            if transport: transport.close()

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--bind', default='0.0.0.0')
    parser.add_argument('--port',type=int,default=6000)
    parser.add_argument('--delay',type=float,default=100,help='Stall duration in ms (default: 100); per-input delay in continuous mode')
    parser.add_argument('--jitter',type=float,default=0)
    parser.add_argument('--loss',type=float,default=0)
    parser.add_argument('--delay-mode', choices=('intermittent','continuous'), default='intermittent',help='Default: intermittent (normally forward immediately)')
    parser.add_argument('--interval-min', type=float, default=DEFAULT_INTERVAL_MIN_SECONDS, help='Minimum seconds between delay events (default: 8)')
    parser.add_argument('--interval-max', type=float, default=DEFAULT_INTERVAL_MAX_SECONDS, help='Maximum seconds between delay events (default: 10)')
    args=parser.parse_args()
    import math
    if not 1024<=args.port<=65534 or args.port%2 or not all(math.isfinite(v) for v in (args.delay,args.jitter,args.loss)) or not 0<=args.delay<=10000 or not 0<=args.jitter<=10000 or not 0<=args.loss<=100:
        parser.error('port must be even, 1024..65534; delay/jitter 0..10000ms; loss 0..100%')
    if not math.isfinite(args.interval_min) or not math.isfinite(args.interval_max) or not 0 < args.interval_min <= args.interval_max:
        parser.error('intervals must be finite and 0 < interval-min <= interval-max')
    logging.basicConfig(level=logging.INFO,format='%(asctime)s %(message)s')
    try: asyncio.run(serve(args))
    except KeyboardInterrupt: pass
    except OSError as error: parser.exit(1, f'Cannot bind relay ports: {error}\n')
if __name__=='__main__': main()
