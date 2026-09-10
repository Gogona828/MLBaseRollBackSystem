import asyncio
import unittest
import json
from unittest.mock import patch
from server import Relay, Port, PACKET, DelayEvents

class Client(asyncio.DatagramProtocol):
    def __init__(self): self.received=asyncio.Queue()
    def datagram_received(self,data,address): self.received.put_nowait(data)

class RelayTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.relay=Relay(delay=25, mode='continuous')
        self.open=[]; self.ports=[]; self.clients=[]
        loop=asyncio.get_running_loop()
        for slot in (0,1):
            t,_=await loop.create_datagram_endpoint(lambda slot=slot:Port(self.relay,slot),local_addr=('127.0.0.1',0))
            self.open.append(t); self.ports.append(t.get_extra_info('sockname'))
            c,p=await loop.create_datagram_endpoint(Client,local_addr=('127.0.0.1',0))
            self.open.append(c); self.clients.append((c,p))
    async def asyncTearDown(self):
        for h in self.relay.pending:h.cancel()
        for t in self.open:t.close()
    def send(self,slot,kind,frame=-1,bits=0):
        packet=PACKET.pack(kind,slot,frame,bits,60)
        self.clients[slot][0].sendto(packet,self.ports[slot]);return packet
    async def register(self):
        self.send(0,1);self.send(1,1)
        await asyncio.sleep(.06)
        for _,p in self.clients:
            while not p.received.empty():p.received.get_nowait()
    async def test_bidirectional_bytes_and_delay(self):
        await self.register()
        for slot in (0,1):
            start=asyncio.get_running_loop().time()
            sent=self.send(slot,4,42,5)
            actual=await asyncio.wait_for(self.clients[1-slot][1].received.get(),1)
            self.assertEqual(sent,actual)
            self.assertGreaterEqual(asyncio.get_running_loop().time()-start,.02)
    async def test_start_echo_and_loss(self):
        await self.register();self.relay.loss=100
        start=self.send(0,3)
        for _,p in self.clients:self.assertEqual(start,await asyncio.wait_for(p.received.get(),1))
        await asyncio.sleep(.2)
        for _,p in self.clients:
            while not p.received.empty():p.received.get_nowait()
        self.send(0,4,0,4)
        with self.assertRaises(asyncio.TimeoutError):await asyncio.wait_for(self.clients[1][1].received.get(),.1)
    async def test_round_agreement_uses_same_ports(self):
        await self.register()
        message=b'RRA1{"signature":{"senderPlayerId":0}}'
        self.clients[0][0].sendto(message,self.ports[0])
        actual=await asyncio.wait_for(self.clients[1][1].received.get(),1)
        self.assertEqual(message,actual)
    def send_control(self, slot, **message):
        self.clients[slot][0].sendto(b'RRA1'+json.dumps(message).encode(),self.ports[slot])
    async def get_control(self, slot):
        data=await asyncio.wait_for(self.clients[slot][1].received.get(),.5)
        return json.loads(data[4:])
    async def test_round_barrier_waits_for_both_and_retries_same_deadline(self):
        await self.register()
        self.send_control(0,kind='roundReady',round=1,frame=400)
        with self.assertRaises(asyncio.TimeoutError):
            await asyncio.wait_for(self.clients[0][1].received.get(),.04)
        self.send_control(1,kind='roundReady',round=1,frame=407)
        p1,p2=await self.get_control(0),await self.get_control(1)
        self.assertEqual(p1,p2)
        self.assertEqual(p1['nextFrame'],408)
        self.assertGreater(p1['startAt'],asyncio.get_running_loop().time()+.8)
        self.send_control(0,kind='roundReady',round=1,frame=500)
        self.assertEqual(await self.get_control(0),p1)
        self.assertEqual(await self.get_control(1),p1)
        self.send_control(0,kind='roundReady',round=2,frame=800)
        with self.assertRaises(asyncio.TimeoutError):
            await asyncio.wait_for(self.clients[0][1].received.get(),.04)
    async def test_control_bypasses_delay_event(self):
        await self.register()
        self.relay.delay_events=DelayEvents(10000,0,'intermittent',8,10)
        self.relay.delay_events.release_at=asyncio.get_running_loop().time()+10
        self.send_control(0,kind='clock',clientTime=12.5)
        result=await self.get_control(0)
        self.assertEqual(result['clientTime'],12.5)
        self.assertIn('serverTime',result)
    async def test_intermittent_stall_preserves_input_order_in_both_directions(self):
        await self.register()
        self.relay.delay_events=DelayEvents(60,0,'intermittent',8,10)
        self.relay.delay_events.next_event=asyncio.get_running_loop().time()-1
        begin=asyncio.get_running_loop().time()
        for slot in (0,1): self.send(slot,4,10,1)
        await asyncio.sleep(.01)
        for slot in (0,1): self.send(slot,4,11,2)
        for slot in (0,1):
            first=await asyncio.wait_for(self.clients[slot][1].received.get(),.5)
            second=await asyncio.wait_for(self.clients[slot][1].received.get(),.5)
            self.assertEqual(PACKET.unpack(first)[2],10)
            self.assertEqual(PACKET.unpack(second)[2],11)
        self.assertGreaterEqual(asyncio.get_running_loop().time()-begin,.05)
        self.assertEqual(self.relay.delay_events.wait_seconds(asyncio.get_running_loop().time()),0)

    async def test_invalid_and_slot_collision(self):
        await self.register()
        self.clients[0][0].sendto(b'bad',self.ports[1])
        self.clients[0][0].sendto(PACKET.pack(1,1,-1,0,0),self.ports[1])
        await asyncio.sleep(.05)
        self.assertEqual(self.relay.peers[1],self.clients[1][0].get_extra_info('sockname'))

class DelayEventTests(unittest.TestCase):
    def test_default_interval_and_release(self):
        with patch('server.random.uniform',side_effect=lambda low,high:(low+high)/2):
            events=DelayEvents(100,0,'intermittent',8,10)
            self.assertEqual(events.wait_seconds(0),0)
            self.assertEqual(events.next_event,9)
            self.assertEqual(events.wait_seconds(8.99),0)
            self.assertAlmostEqual(events.wait_seconds(9),.1)
            self.assertAlmostEqual(events.wait_seconds(9.05),.05)
            self.assertEqual(events.wait_seconds(9.11),0)
            self.assertEqual(events.next_event,18)
    def test_intervals_stay_in_requested_range(self):
        events=DelayEvents(100,0,'intermittent',8,10)
        events.wait_seconds(0)
        previous=0
        for _ in range(100):
            event=events.next_event
            self.assertGreaterEqual(event-previous,8)
            self.assertLessEqual(event-previous,10)
            events.wait_seconds(event)
            previous=event

if __name__=='__main__':unittest.main()
