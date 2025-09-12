using System;
using System.Collections.Generic;
using System.Text;

namespace flashkit_md
{
    class Cart
    {
        public const int MAP_2M = 1;
        public const int MAP_3M = 2;
        public const int MAP_SSF = 3;

        // Bank / page configuration
        // ROM is split into 512KB pages. Console sees 4MB window split into 512KB banks.
        // Bank 0 is fixed to page 0 (boot).
        private const int PageSize = 0x80000; // 512KB
        private const int BankCount = 8;       // 4MB / 512KB

        // Bank register addresses (bank 0 has no register - fixed to page 0).
        private static readonly int[] BankRegisterAddrs = new int[BankCount]
        {
            0x000000,       // bank 0 - no register (fixed to first page)
            0xA130F3,       // bank 1 -> $080000-$0FFFFF
            0xA130F5,       // bank 2 -> $100000-$17FFFF
            0xA130F7,       // bank 3 -> $180000-$1FFFFF
            0xA130F9,       // bank 4 -> $200000-$27FFFF
            0xA130FB,       // bank 5 -> $280000-$2FFFFF
            0xA130FD,       // bank 6 -> $300000-$37FFFF
            0xA130FF        // bank 7 -> $380000-$3FFFFF
        };

        // Current page mapped into each bank (page numbers are byte values).
        // bankPages[0] is always 0 (first page) by design.
        private static readonly byte[] bankPages = new byte[BankCount];

        static Cart()
        {
            // Ensure bank 0 is fixed to page 0 on startup.
            bankPages[0] = 0;
            // Attempt to read current hardware registers for banks 1..7
            try
            {
                ReadBankRegisters();
            }
            catch (Exception)
            {
                // If device is not available or read fails, leave defaults (0)
            }
        }

        // Read a single byte from an arbitrary byte-addressable register.
        // Device.setAddr expects a byte address and internally divides by 2,
        // so we align to the even word boundary and then read two bytes,
        // returning the requested byte (even -> offset 0, odd -> offset 1).
        private static byte ReadRegisterByte(int addr)
        {
            int aligned = addr & ~1;
            Device.setAddr(aligned);
            byte[] buf = new byte[2];
            Device.read(buf, 0, 2);
            return buf[addr & 1];
        }

        // Write a single byte to a register (works for odd/even addresses).
        private static void WriteRegisterByte(int addr, byte value)
        {
            Device.writeByte(addr, value);
        }

        // Populate bankPages[] by reading hardware registers for banks 1..7.
        public static void ReadBankRegisters()
        {
            // bank 0 is always 0
            bankPages[0] = 0;

            for (int b = 1; b < BankCount; b++)
            {
                int regAddr = BankRegisterAddrs[b];
                if (regAddr == 0) { bankPages[b] = 0; continue; }
                bankPages[b] = ReadRegisterByte(regAddr);
            }
        }

        // Get the page mapped to given bank (0..7).
        public static byte GetBankPage(int bankIndex)
        {
            if (bankIndex < 0 || bankIndex >= BankCount) throw new ArgumentOutOfRangeException(nameof(bankIndex));
            return bankPages[bankIndex];
        }

        // Set a page into a given bank (bank 1..7). Bank 0 is fixed and cannot be changed.
        // This writes to the corresponding hardware register and updates internal state.
        public static void SetBankPage(int bankIndex, byte page)
        {
            if (bankIndex < 0 || bankIndex >= BankCount) throw new ArgumentOutOfRangeException(nameof(bankIndex));
            if (bankIndex == 0) throw new InvalidOperationException("Bank 0 is fixed to the first page and cannot be changed.");

            int regAddr = BankRegisterAddrs[bankIndex];
            if (regAddr == 0) throw new InvalidOperationException("No register defined for this bank.");

            WriteRegisterByte(regAddr, page);
            bankPages[bankIndex] = page;
        }

        // Write all current bankPages[] values back to the hardware registers (except bank 0).
        public static void ApplyBanksToDevice()
        {
            for (int b = 1; b < BankCount; b++)
            {
                int regAddr = BankRegisterAddrs[b];
                if (regAddr == 0) continue;
                WriteRegisterByte(regAddr, bankPages[b]);
            }
        }

        // Helper: compute the ROM byte offset of a given page.
        public static long PageToOffset(byte page)
        {
            return (long)page * PageSize;
        }

        // Helper: compute the console-visible address range for a bank.
        public static (int start, int end) BankAddressRange(int bankIndex)
        {
            if (bankIndex < 0 || bankIndex >= BankCount) throw new ArgumentOutOfRangeException(nameof(bankIndex));
            int start = bankIndex * PageSize;
            int end = start + PageSize - 1;
            return (start, end);
        }

        // Read a block of registers from the A13000 range (or any range) for inspection.
        // startAddr and length are byte-addressed.
        public static byte[] ReadRegistersRange(int startAddr, int length)
        {
            if (length <= 0) return new byte[0];

            // align start to even boundary
            int alignedStart = startAddr & ~1;
            int extra = startAddr - alignedStart;
            int readLen = length + extra;

            // ensure readLen is even (Device.read works with any byte length; setAddr alignment is handled)
            Device.setAddr(alignedStart);
            byte[] buf = new byte[readLen];
            Device.read(buf, 0, buf.Length);

            // return the requested slice
            byte[] outBuf = new byte[length];
            Array.Copy(buf, extra, outBuf, 0, length);
            return outBuf;
        }

        // Existing methods below remain unchanged but can use bank helper methods if needed.

        static string getRomRegion(byte[] rom_hdr)
        {

            byte val = rom_hdr[0x1f0];
            if (val != rom_hdr[0x1f1] && rom_hdr[0x1f1] != 0x20 && rom_hdr[0x1f1] != 0) return "W";

            switch (val)
            {

                case (byte)'F':
                case (byte)'C':
                    return "W";

                case (byte)'U':
                case (byte)'W':
                case (byte)'4':
                case 4:
                    return "U";

                case (byte)'J':
                case (byte)'B':
                case (byte)'1':
                case 1:
                    return "J";

                case (byte)'E':
                case (byte)'A':
                case (byte)'8':
                case 8:
                    return "E";

            }

            return "X";

        }

        public static string getRomName()
        {
            string name = null;
            byte[] rom_hdr = new byte[512];
            Device.setAddr(0);
            Device.read(rom_hdr, 0, 512);

            name = getRomName(0x120, rom_hdr);

            if (name == null) name = getRomName(0x150, rom_hdr);

            if (name == null) name = "Unknown";

            name += " (" + getRomRegion(rom_hdr) + ")";

            return name;
        }

        static string getRomName(int offset, byte[] buff)
        {
            string name = "";
            int name_empty = 1;


            for (int i = offset + 47; i >= offset; i--)
            {
                if (buff[i] != 0 & buff[i] != 0x20) break;
                if (buff[i] == 0x20) buff[i] = 0;
            }

            for (int i = offset; i < offset + 48; i++)
            {
                if (buff[i] == 0) break;
                if (buff[i] == '/' || buff[i] == ':') buff[i] = (byte)'-';
                try
                {
                    name += (char)buff[i];
                    if (buff[i] != 0x20) name_empty = 0;
                    if (buff[i] == ' ' || buff[i] == '!' || buff[i] == '(' || buff[i] == ')' || buff[i] == '_' || buff[i] == '-') continue;
                    if (buff[i] == '.' || buff[i] == '[' || buff[i] == ']' || buff[i] == '|') continue;
                    if (buff[i] == '&' || buff[i] == 0x27 || buff[i] == 0x60) continue;
                    if (buff[i] >= '0' && buff[i] <= '9') continue;
                    if (buff[i] >= 'A' && buff[i] <= 'Z') continue;
                    if (buff[i] >= 'a' && buff[i] <= 'z') continue;

                    throw new Exception("name error");
                }
                catch (Exception)
                {
                    return null;
                }
            }

            if (name_empty != 0) return null;
            return name;
        }

        static int checkRomSize(int base_addr, int max_len)
        {
            int eq;
            int base_len = 0x8000;
            int len = 0x8000;
            byte[] sector0 = new byte[256];
            byte[] sector = new byte[256];
            Device.writeWord(0xA13000, 0x0000);

            Device.setAddr(base_addr);
            Device.read(sector0, 0, sector.Length);

            for (; ; )
            {

                Device.setAddr(base_addr + len);
                Device.read(sector, 0, sector.Length);

                eq = 1;
                for (int i = 0; i < sector.Length; i++) if (sector0[i] != sector[i]) eq = 0;
                if (eq == 1) break;

                len *= 2;
                if (len >= max_len) break;
            }

            if (len == base_len) return 0;
            return len;
        }

        public static int GetFullRomSize()
        {
            // Try to detect the number of pages by probing each page for unique data.
            // If you know the max is always 16, you can just return 16 * PageSize.
            int maxPages = 16;
            int detectedPages = 1;

            byte[] buf0 = new byte[16];
            byte[] buf = new byte[16];

            // Read the first 16 bytes of page 0 as reference
            Device.writeWord(0xA13000, 0x0000); // Set banks to default
            Device.setAddr(0x000000);
            Device.read(buf0, 0, buf0.Length);

            for (int page = 1; page < maxPages; page++)
            {
                // Map page into bank 1 (bank 0 is always page 0)
                SetBankPage(1, (byte)page);
                Device.setAddr(0x080000); // Bank 1 address
                Device.read(buf, 0, buf.Length);

                bool different = false;
                for (int i = 0; i < buf.Length; i++)
                {
                    if (buf[i] != buf0[i])
                    {
                        different = true;
                        break;
                    }
                }
                if (different)
                    detectedPages = page + 1;
                else
                    break;
            }

            // Restore bank 1 to page 1 (or 0 if you want to be safe)
            SetBankPage(1, 1);

            return detectedPages * PageSize;
        }

        public static int getRomSize()
        {


            byte[] sector0 = new byte[512];
            byte[] sector = new byte[512];
            int ram = 0;
            int extra_rom = 0;

            if (ramAvailable())
            {
                ram = 1;
                extra_rom = 1;
                Device.writeWord(0xA13000, 0x0000);
                Device.setAddr(0x200000);
                Device.read(sector0, 0, 512);
                Device.setAddr(0x200000);
                Device.read(sector, 0, 512);
                for (int i = 0; i < sector.Length; i++) if (sector[i] != sector0[i]) extra_rom = 0;

                if (extra_rom != 0)
                {
                    extra_rom = 0;
                    Device.setAddr(0x200000 + 0x10000);
                    Device.read(sector, 0, 512);

                    Device.writeWord(0xA13000, 0xffff);
                    Device.setAddr(0x200000);
                    Device.read(sector, 0, 512);
                    for (int i = 0; i < sector.Length; i++) if (sector[i] != sector0[i]) extra_rom = 1;

                }

            }

            int max_rom_size = ram != 0 && extra_rom == 0 ? 0x200000 : 0x400000;
            int len = checkRomSize(0, max_rom_size);

            if (len == 0x400000)
            {
                len = 0x200000;
                int len2 = checkRomSize(0x200000, 0x200000);
                if (len2 == 0x200000)
                {
                    len2 = checkRomSize(0x300000, 0x100000);
                    len2 = len2 >= 0x80000 ? 0x200000 : 0x100000;
                }
                if (len2 >= 0x80000) len += len2;
            }

            return len;

        }

        static bool ramAvailable()
        {
            int first_word;
            UInt16 tmp;

            Device.writeWord(0xA13000, 0xffff);

            first_word = Device.readWord(0x200000);
            Device.writeWord(0x200000, (UInt16)(first_word ^ 0xffff));
            tmp = Device.readWord(0x200000);
            Device.writeWord(0x200000, (UInt16)first_word);
            tmp ^= 0xffff;
            if ((first_word & 0x00ff) != (tmp & 0x00ff)) return false;

            return true;
        }

        public static int getRamSize()
        {
            int ram_szie = 256;
            int first_word;
            int first_word_tmp;
            UInt16 tmp;
            UInt16 tmp2;

            int ram_type = 0x00ff;

            //Device.writeWord(0xA13000, 0x0001);

            if (!ramAvailable()) return 0;

            first_word = Device.readWord(0x200000);

            while (ram_szie < 0x100000)
            {
                tmp = Device.readWord(0x200000 + ram_szie);
                Device.writeWord(0x200000 + ram_szie, (UInt16)(tmp ^ 0xffff));
                tmp2 = Device.readWord(0x200000 + ram_szie);
                first_word_tmp = Device.readWord(0x200000);
                Device.writeWord(0x200000 + ram_szie, tmp);
                tmp2 ^= 0xffff;
                if ((tmp & 0xff) != (tmp2 & 0xff)) break;
                if ((first_word & ram_type) != (first_word_tmp & ram_type)) break;
                ram_szie *= 2;
            }

            return ram_szie / 2;
        }

        public static byte[] getRam()
        {
            byte[] buff = null;


            int ram_size = 0;
            ram_size = Cart.getRamSize() * 2;

            buff = new byte[ram_size];

            Device.writeWord(0xA13000, 0xffff);
            Device.setAddr(0x200000);

            Device.read(buff, 0, buff.Length);

            return buff;
        }


    }
}