using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace flashkit_md
{
    public class Helpers
    {
        public static string getRomName(int offset, byte[] rom_hdr)
        {
            StringBuilder name = new StringBuilder(48);
            char c;
            for (int i = 0; i < 48; i++)
            {
                c = (char)rom_hdr[offset + i];
                if (c < 32 || c > 126) break;
                name.Append(c);
            }
            if (name.Length == 0) return null;
            return name.ToString().Trim();
        }
    }
}
