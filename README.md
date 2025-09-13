FlashKit MX29GL128E Bootleg Edition

This fork of krikzz/flashkit adds support for Sega Mega Drive / Genesis bootleg cartridges that use the Macronix MX29GL128E flash chip.

Features

- Bank switching support – correctly handles the CPLD mapper used in bootleg boards.

- Read and write up to 8 MB ROMs on MX29GL128E-based carts.

- Erase and flash support – full sector erase and buffer writes for faster programming.

- ROM size detection with trimming to avoid oversized dumps.

Notes

This fork is focused on bootleg cartridges only, and functionality may be limited to MX29GL128E hardware.

Smaller ROMs will still be padded to the bank window size unless trimmed.
