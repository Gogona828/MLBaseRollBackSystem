#!/usr/bin/env python3
"""Two-player IPv4 UDP relay. Even base port = P1, base+1 = P2."""
import argparse
import asyncio
import logging
import random
import socket
import struct
import time

PACKET = struct.Struct('<BiiBi')

class Relay:
    def __init__(self, delay=100, jitter=0, loss=0, timeout=15):
        self.delay, self.jitter, self.loss, self.timeout = delay, jitter, loss, timeout
        self.peers = [None, None]
        self.seen = [0., 0.]
        self.transports = [None, None]
        self.generation = 0
        self.pending = set()

    def receive(self, slot, data, address):
        if data.startswith(b'RRA1') and 4 < len(data) <= 4100:
            if self.peers[slot] == address and self.peers[1-slot]:
                self.seen[slot] = time.monotonic()
                self.schedule(1-slot, data, self.delay/1000)
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
        for destination in targets:
            if kind == 4 and random.random()*100 < self.loss:
                continue
            delay = max(0, self.delay+random.uniform(-self.jitter,self.jitter))/1000
            for repeat in range(3 if kind == 3 else 1):
                self.schedule(destination, data, delay+repeat*.05)

    def schedule(self, slot, data, delay):
        if len(self.pending) >= 8192:
            return
        generation, address = self.generation, self.peers[slot]
        handle = None
        def send():
            self.pending.discard(handle)
            if generation == self.generation and address == self.peers[slot]:
                self.transports[slot].sendto(data,address)
        handle = asyncio.get_running_loop().call_later(delay,send)
        self.pending.add(handle)

class Port(asyncio.DatagramProtocol):
    def __init__(self, relay, slot): self.relay, self.slot = relay, slot
    def connection_made(self, transport): self.relay.transports[self.slot] = transport
    def datagram_received(self, data, address): self.relay.receive(self.slot,data,address)
    def error_received(self, error): logging.warning('UDP: %s', error)

async def serve(args):
    relay = Relay(args.delay,args.jitter,args.loss)
    loop = asyncio.get_running_loop()
    try:
        for slot in (0,1):
            await loop.create_datagram_endpoint(lambda slot=slot: Port(relay,slot),local_addr=(args.bind,args.port+slot),family=socket.AF_INET)
        logging.info('Ready: P1 UDP %s / P2 UDP %s, one-way delay %sms +/- %sms, input loss %s%%',args.port,args.port+1,args.delay,args.jitter,args.loss)
        await asyncio.Future()
    finally:
        for handle in relay.pending: handle.cancel()
        for transport in relay.transports:
            if transport: transport.close()

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--bind', default='0.0.0.0')
    parser.add_argument('--port',type=int,default=6000)
    parser.add_argument('--delay',type=float,default=100)
    parser.add_argument('--jitter',type=float,default=0)
    parser.add_argument('--loss',type=float,default=0)
    args=parser.parse_args()
    import math
    if not 1024<=args.port<=65534 or args.port%2 or not all(math.isfinite(v) for v in (args.delay,args.jitter,args.loss)) or not 0<=args.delay<=10000 or not 0<=args.jitter<=10000 or not 0<=args.loss<=100:
        parser.error('port must be even, 1024..65534; delay/jitter 0..10000ms; loss 0..100%')
    logging.basicConfig(level=logging.INFO,format='%(asctime)s %(message)s')
    try: asyncio.run(serve(args))
    except KeyboardInterrupt: pass
    except OSError as error: parser.exit(1, f'Cannot bind relay ports: {error}\n')
if __name__=='__main__': main()
