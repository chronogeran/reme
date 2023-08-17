namespace Utils
{
	public static class AddressExtensions
	{
		public static int ToPCAddress(this string snesAddressHex)
		{
			return ToPCAddress(int.Parse(snesAddressHex, System.Globalization.NumberStyles.HexNumber));
		}

		public static int ToPCAddress(this int snesAddress)
		{
			byte bank = (byte)(snesAddress >> 16);
			if (bank >= 0xc0)
				bank -= 0xc0;
			return (bank << 16) + (snesAddress & 0x00ffff);
		}

		public static int ToSNESAddress(this int pcAddress)
		{
			byte bank = (byte)(pcAddress >> 16);
			if (bank < 0x40)
				bank += 0xc0;
			return (bank << 16) + (pcAddress & 0x00ffff);
		}
	}
}
