using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace flashkit_md
{
    public class MX29GL128Helpers
    {
        private readonly DeviceSession session;
        private readonly Action<string> log;
        private const int TotalChipSize = 0x800000; // Chip is 128Mb but mapper can see only half
        private const int SectorSize = 0x20000;   // 128KB
        private const int BankWindow = 0x080000;  // 512KB visible window
        private const int BufferSize = 64;        // 32 words

        public MX29GL128Helpers(DeviceSession session, Action<string> log)
        {
            this.session = session;
            this.log = log;
        }

        public void FlashWaitEraseReady(int addr)
        {
            const int timeoutMs = 10000;
            var sw = Stopwatch.StartNew();

            while (true)
            {
                ushort val = session.ReadWord(addr);

                // DQ7 = 1 indicates ready
                if ((val & 0x80) == 0x80)
                    return;

                if (sw.ElapsedMilliseconds > timeoutMs)
                    throw new TimeoutException($"Flash erase timeout at 0x{addr:X6}");

                Thread.Sleep(1);
            }
        }

        public void FlashWaitWordReady(int addr, ushort expected)
        {
            const int timeoutMs = 5000;
            var sw = Stopwatch.StartNew();

            while (true)
            {
                ushort val = session.ReadWord(addr);
                if ((val & 0x80) == (expected & 0x80))
                    break;

                if (sw.ElapsedMilliseconds > timeoutMs)
                    throw new TimeoutException($"Flash write timeout at 0x{addr:X6}");
            }
        }

        public void FlashWaitBufferReady(int addr, ushort expected)
        {
            const int timeoutMs = 5000;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            while (true)
            {
                ushort val = session.ReadWord(addr);

                if ((val & 0x80) == (expected & 0x80)) // DQ7 = ready
                    break;

                if (sw.ElapsedMilliseconds > timeoutMs)
                    throw new TimeoutException($"Flash buffer write timeout at 0x{addr:X6}");
            }
        }

        public void FlashWriteBuffer(byte[] buff, int offset, int length, int addr)
        {
            if (length % 2 != 0 || length > BufferSize)
                throw new ArgumentException($"Buffer writes must be even and <= {BufferSize} bytes.");

            int wordCount = length / 2;

            int bank = addr / BankWindow;
            int pageOffset = addr % BankWindow;
            int baseAddr = (bank > 0) ? BankWindow + pageOffset : addr;
            int unlockBase = (bank > 0) ? BankWindow : 0;

            // Sector base = align current address down to sector boundary (128 KB aligned)
            int sectorBase = baseAddr & ~(SectorSize - 1);

            // Unlock sequence
            session.WriteWord(unlockBase + 0x555 * 2, 0xAA);
            session.WriteWord(unlockBase + 0x2AA * 2, 0x55);

            // Issue Write Buffer Load command at sector base
            session.WriteWord(sectorBase, 0x25);
            session.WriteWord(sectorBase, (ushort)(wordCount - 1));

            // Load all words into buffer at their actual addresses
            for (int i = 0; i < wordCount; i++)
            {
                ushort word = (ushort)((buff[offset + i * 2] << 8) | buff[offset + i * 2 + 1]);
                session.WriteWord(baseAddr + i * 2, word);
            }

            // Confirm buffer programming at sector base
            session.WriteWord(sectorBase, 0x29);

            // Wait until last word signals ready
            int lastAddr = baseAddr + (wordCount - 1) * 2;
            ushort lastWord = (ushort)((buff[offset + (wordCount - 1) * 2] << 8) |
                                       buff[offset + (wordCount - 1) * 2 + 1]);
            FlashWaitBufferReady(lastAddr, lastWord);
        }

        public void EraseAllSectors(Action<string> log, DeviceSession session)
        {
            int totalSectors = TotalChipSize / SectorSize; // e.g. 0x800000 / 0x20000 = 64

            for (int sector = 0; sector < totalSectors; sector++)
            {
                int addr = sector * SectorSize;

                int bank = addr / BankWindow;
                int pageOffset = addr % BankWindow;
                int baseAddr = (bank > 0) ? BankWindow + pageOffset : addr;
                int unlockBase = (bank > 0) ? BankWindow : 0;

                if (bank > 0)
                    Cart.SetBankPage(1, (byte)bank);

                // Unlock sequence
                session.WriteWord(unlockBase + 0x555 * 2, 0xAA);
                session.WriteWord(unlockBase + 0x2AA * 2, 0x55);
                session.WriteWord(unlockBase + 0x555 * 2, 0x80);
                session.WriteWord(unlockBase + 0x555 * 2, 0xAA);
                session.WriteWord(unlockBase + 0x2AA * 2, 0x55);

                // Sector erase command
                session.WriteWord(baseAddr, 0x30);

                // Wait for completion
                FlashWaitEraseReady(baseAddr);

                #if DEBUG
                log?.Invoke($"Erased sector {sector:D2} at 0x{addr:X6}");
                #endif
            }
        }

        public void WriteROM(byte[] rom, Action<string> log, DeviceSession session, Action<int> progressUpdate)
        {
            int romSize = rom.Length;

            for (int addr = 0; addr < romSize;)
            {
                int bank = addr / BankWindow;
                int pageOffset = addr % BankWindow;
                int remainingInBank = BankWindow - pageOffset;
                int remainingInRom = romSize - addr;

                // Prefer buffer-sized chunks (64 bytes), fall back to smaller if necessary
                int chunk = Math.Min(BufferSize, Math.Min(remainingInBank, remainingInRom));

                int baseAddr = (bank > 0) ? BankWindow + pageOffset : addr;
                int unlockBase = (bank > 0) ? BankWindow : 0;

                if (bank > 0)
                    Cart.SetBankPage(1, (byte)bank);

                if (chunk == BufferSize && pageOffset % BufferSize == 0)
                {
                    // Full buffer write (fast path)
                    FlashWriteBuffer(rom, addr, chunk, baseAddr);
                }
                else
                {
                    // Fallback: program word by word
                    for (int i = 0; i < chunk; i += 2)
                    {
                        int writeAddr = baseAddr + i;

                        // Unlock sequence for single-word program
                        session.WriteWord(unlockBase + 0x555 * 2, 0xAA);
                        session.WriteWord(unlockBase + 0x2AA * 2, 0x55);

                        // Handle odd-length ROMs (pad last byte with 0xFF)
                        ushort w = 0xFFFF;
                        if (addr + i + 1 < romSize)
                            w = (ushort)((rom[addr + i] << 8) | rom[addr + i + 1]);
                        else if (addr + i < romSize)
                            w = (ushort)(rom[addr + i] << 8);

                        session.WriteWord(writeAddr, w);
                        FlashWaitWordReady(writeAddr, w);
                    }
                }

                addr += chunk;

                #if DEBUG
                // Log once per 128KB sector
                if (addr % SectorSize == 0 || addr == romSize)
                    log?.Invoke($"Flashed sector 0x{addr - chunk:X6} - 0x{addr - 1:X6}");
                #endif
                // Progress update every 4KB
                if (addr % 0x1000 == 0 || addr == romSize)
                    progressUpdate?.Invoke(addr);

            }
        }

        public void ReadRom(string filePath, int romSize, Action<string> log, Action<int> progress = null)
        {
            const int bankWindow = 0x080000; // 512KB window for bank 1
            const int blockSize = 32768;     // read in 32KB chunks
            int pages = (romSize + bankWindow - 1) / bankWindow;

            log?.Invoke("Starting ROM read...");

            using (MemoryStream ms = new MemoryStream())
            {
                byte[] pageBuf = new byte[bankWindow];

                for (int p = 0; p < pages; p++)
                {
                    int bytesRemaining = Math.Min(bankWindow, romSize - p * bankWindow);

                    if (p == 0)
                    {
                        // bank 0 is fixed
                        session.SetAddr(0x000000);
                    }
                    else
                    {
                        // map bank into window
                        Cart.SetBankPage(1, (byte)p);
                        session.SetAddr(bankWindow);
                    }

                    int offset = 0;
                    while (offset < bytesRemaining)
                    {
                        int chunk = Math.Min(blockSize, bytesRemaining - offset);
                        session.Read(pageBuf, offset, chunk);
                        offset += chunk;
                    }

                    ms.Write(pageBuf, 0, bytesRemaining);

                    progress?.Invoke(p * bankWindow + bytesRemaining);

                    #if DEBUG
                    log?.Invoke($"Read page {p} ({bytesRemaining / 1024} KB)");
                    #endif
                }

                // Trim trailing 0xFFs
                byte[] fullData = ms.ToArray();
                int lastNonFF = fullData.Length - 1;
                while (lastNonFF >= 0 && fullData[lastNonFF] == 0xFF)
                    lastNonFF--;

                int trimmedSize = lastNonFF + 1;
                byte[] trimmed = new byte[trimmedSize];
                Array.Copy(fullData, trimmed, trimmedSize);

                File.WriteAllBytes(filePath, trimmed);
            }
            log?.Invoke("ROM read complete");
        }

        public void VerifyROM(byte[] rom, Action<string> log, DeviceSession session, Action<int> progressUpdate)
        {
            int romSize = rom.Length;
            byte[] verifyBuf = new byte[0x1000];

            for (int addr = 0; addr < romSize; addr += 0x1000)
            {
                int bank = addr / BankWindow;
                int pageOffset = addr % BankWindow;
                int baseAddr = (bank > 0) ? BankWindow + pageOffset : addr;

                if (bank > 0)
                    Cart.SetBankPage(1, (byte)bank);

                session.SetAddr(baseAddr);
                int readLen = Math.Min(0x1000, romSize - addr);
                session.Read(verifyBuf, 0, readLen);

                for (int i = 0; i < readLen; i++)
                {
                    if (rom[addr + i] != verifyBuf[i])
                        throw new Exception($"Verify error at 0x{addr + i:X6}: wrote {rom[addr + i]:X2}, read {verifyBuf[i]:X2}");
                }

                if (addr % 0x2000 == 0)
                    progressUpdate?.Invoke(addr);
            }
        }

    }
}
