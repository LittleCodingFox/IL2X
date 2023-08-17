using System;

namespace SimpleTest1
{
    struct AStruct
    {
        public int t;
        private int t2;
    }

    class A
    {
        public int i;
        private int j;
        protected int k;

        public A(int i) { this.i = i; }

        public A()
        {
            i = 0;
        }

        public void Add(int b)
        {
            i += b;
        }

        private void Remove(int c)
        {
            i -= c;
        }

        protected void Print()
        {
        }
    }

    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");
        }
    }
}