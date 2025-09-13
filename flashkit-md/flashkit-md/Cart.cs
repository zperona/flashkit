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

        public static int GetRomSize()
        {
            const int BankWindow = 0x080000; // 512KB
            const int MaxBanks = 16;         // 8MB mapped by the CPLD / 512KB
            byte[] buf = new byte[512];

            int lastNonBlankBank = -1;

            // Step 1: Find last non-blank bank
            for (int bank = 0; bank < MaxBanks; bank++)
            {
                if (bank == 0)
                {
                    Device.setAddr(0x000000);
                }
                else
                {
                    SetBankPage(1, (byte)bank);
                    Device.setAddr(BankWindow);
                }

                Device.read(buf, 0, buf.Length);

                bool allFF = true;
                for (int i = 0; i < buf.Length; i++)
                {
                    if (buf[i] != 0xFF)
                    {
                        allFF = false;
                        break;
                    }
                }

                if (!allFF)
                    lastNonBlankBank = bank;
            }

            if (lastNonBlankBank < 0)
                return 0; // no ROM found at all

            // Step 2: Scan the last bank fully to find the last non-0xFF
            byte[] fullBank = new byte[BankWindow];
            if (lastNonBlankBank == 0)
            {
                Device.setAddr(0x000000);
            }
            else
            {
                SetBankPage(1, (byte)lastNonBlankBank);
                Device.setAddr(BankWindow);
            }
            Device.read(fullBank, 0, fullBank.Length);

            int lastUsedOffset = 0;
            for (int i = fullBank.Length - 1; i >= 0; i--)
            {
                if (fullBank[i] != 0xFF)
                {
                    lastUsedOffset = i + 1;
                    break;
                }
            }

            int romSize = lastNonBlankBank * BankWindow + lastUsedOffset;
            return romSize;
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