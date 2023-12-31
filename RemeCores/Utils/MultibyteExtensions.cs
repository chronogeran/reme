﻿namespace Utils
{
    public static class MultibyteExtensions
    {
        public static void SetLowByte(ref this ushort val, byte b)
        {
            val = (ushort)((val & 0xff00) | b);
        }
        public static void SetHighByte(ref this ushort val, byte b)
        {
            val = (ushort)((val & 0xff) | (b << 8));
        }
        public static byte GetHighByte(ref this ushort val)
        {
            return (byte)(val >> 8);
        }
    }
}
