import asyncio
import unittest
from server import Relay, Port, PACKET

class Client(asyncio.DatagramProtocol):
    def __init__(self): self.received=asyncio.Queue()
    def datagram_received(self,data,address): self.received.put_nowait(data)

class RelayTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.relay=Relay(delay=25)
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
    async def test_invalid_and_slot_collision(self):
        await self.register()
        self.clients[0][0].sendto(b'bad',self.ports[1])
        self.clients[0][0].sendto(PACKET.pack(1,1,-1,0,0),self.ports[1])
        await asyncio.sleep(.05)
        self.assertEqual(self.relay.peers[1],self.clients[1][0].get_extra_info('sockname'))

if __name__=='__main__':unittest.main()
