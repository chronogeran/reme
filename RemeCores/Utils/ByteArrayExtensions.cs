namespace Utils
{
	public static class ByteArrayExtensions
	{
		public static uint ReadLong(this byte[] bytes, ref int i)
		{
			return (uint)(bytes[++i] + (bytes[++i] << 8) + (bytes[++i] << 16));
		}

		public static uint ReadLong(this byte[] bytes, int i)
		{
			return (uint)(bytes[i] + (bytes[i + 1] << 8) + (bytes[i + 2] << 16));
		}

		public static ushort ReadShort(this byte[] bytes, ref int i)
		{
			return (ushort)(bytes[++i] + (ushort)(bytes[++i] << 8));
		}

		public static ushort ReadShort(this byte[] bytes, int i)
		{
			return (ushort)(bytes[i++] + (ushort)(bytes[i++] << 8));
		}

		public static byte ReadByte(this byte[] bytes, ref int i)
		{
			return bytes[++i];
		}

		public static byte ReadByte(this byte[] bytes, int i)
		{
			return bytes[i];
		}

		public static void WriteShort(this byte[] bytes, ref int i, ushort val)
		{
			bytes[i++] = (byte)(val & 0xff);
			bytes[i++] = (byte)(val >> 8);
		}

		public static void WriteShort(this byte[] bytes, int i, ushort val)
		{
			bytes[i++] = (byte)(val & 0xff);
			bytes[i++] = (byte)(val >> 8);
		}

		public static void WriteLong(this byte[] bytes, int i, int val)
		{
			bytes[i++] = (byte)(val & 0xff);
			bytes[i++] = (byte)(val >> 8);
			bytes[i++] = (byte)(val >> 16);
		}

		public static int IndexOfSequence(this byte[] bytes, byte[] sequence, int startIndex, int searchLength)
		{
			for (int i = 0;
				i + startIndex + sequence.Length < bytes.Length && i < searchLength;
				++i)
			{
				bool found = true;
				for (int j = 0; j < sequence.Length; ++j)
				{
					if (bytes[j + i + startIndex] != sequence[j])
					{
						found = false;
						break;
					}
				}
				if (found)
					return i;
			}
			return -1;
		}
	}
}
