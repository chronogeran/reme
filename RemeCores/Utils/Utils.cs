using System;

namespace Utils
{
	public static class Ut
	{
		public static int ToPCAddress(string snesAddressHex)
		{
			return ToPCAddress(int.Parse(snesAddressHex, System.Globalization.NumberStyles.HexNumber));
		}

		public static int ToPCAddress(int snesAddress)
		{
			byte bank = (byte)(snesAddress >> 16);
			if (bank >= 0xc0)
				bank -= 0xc0;
			return (bank << 16) + (snesAddress & 0x00ffff);
		}

		public static int ToSNESAddress(int pcAddress)
		{
			byte bank = (byte)(pcAddress >> 16);
			if (bank < 0x40)
				bank += 0xc0;
			return (bank << 16) + (pcAddress & 0x00ffff);
		}

		public static int ToLongAddress(byte bank, int shortAddress)
        {
			return (bank << 16) + shortAddress;
		}

		public static uint ReadLong(int i, byte[] bytes)
		{
			return (uint)(bytes[i] + (bytes[i + 1] << 8) + (bytes[i + 2] << 16));
		}

		public static ushort ReadShort(ref int i, byte[] bytes)
		{
			return (ushort)(bytes[++i] + (ushort)(bytes[++i] << 8));
		}

		public static ushort ReadShort(int i, byte[] bytes)
		{
			return (ushort)(bytes[i++] + (ushort)(bytes[i++] << 8));
		}

		public static void WriteShort(ref int i, byte[] bytes, ushort val)
		{
			bytes[i++] = (byte)(val & 0xff);
			bytes[i++] = (byte)(val >> 8);
		}

		public static void WriteShort(int i, byte[] bytes, ushort val)
		{
			bytes[i++] = (byte)(val & 0xff);
			bytes[i++] = (byte)(val >> 8);
		}

		public static byte GetBank(int address)
		{
			return (byte)(address >> 16);
		}
	}
}
