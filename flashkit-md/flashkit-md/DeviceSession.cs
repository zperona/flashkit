using System;

namespace flashkit_md
{
    public class DeviceSession : IDisposable
    {
        private bool connected = false;
        public Device Device { get; private set; }

        public DeviceSession()
        {
            try
            {
                Device = new Device();
                Device.connect();
                Device.setDelay(1);
                connected = true;
            }
            catch
            {
                connected = false;
            }
        }

        public bool IsConnected => connected;

        public void WriteWord(int address, ushort data) => Device.writeWord(address, data);
        public UInt16 ReadWord(int address) => Device.readWord(address);
        public void Read(byte[] buff, int offset, int len) => Device.read(buff, offset, len);
        public void SetDelay(int ms) => Device.setDelay(ms);
        public void SetAddr(int address) => Device.setAddr(address);
        public void FlashResetByPass() => Device.flashResetByPass();
        public void FlashUnlockBypass() => Device.flashUnlockBypass();
        public void Dispose()
        {
            try { Device.disconnect(); }
            catch { }
        }
    }

}
