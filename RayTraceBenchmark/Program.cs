﻿namespace RayTraceBenchmark
{
	public class Program
	{
		private Program p;
		private int i;

		static void Main()
		{
			int a = Foo(123);
			EXIT:;
			if (a == 124) a = -200;
			for (int i = 0; i != 2; ++i)
			{
				for (int i2 = 0; i2 != 2; ++i2)
				{
					if (i2 == 1 && i == 0) a++;
					if (a >= 1) goto EXIT;
				}
			}
		}

		static int Foo(int value)
		{
			return value + 1;
		}

		static void Foo(Program value)
		{
			value.p.i = 123;
		}
	}
}