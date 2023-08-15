using System;

namespace SimpleTest1
{
    class A
    {
        public int i;

        public A(int i) { this.i = i; }

        public A()
        {
            i = 0;
        }

        public void Add(int b)
        {
            i += b;
        }
    }

    internal class Program
    {
        static void Main(string[] args)
        {
            //Console.WriteLine("Hello, World!");
        }
    }
}