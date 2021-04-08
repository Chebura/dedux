using System;
using System.Threading.Tasks;

namespace dedux
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("DEDUX");

            await Task.CompletedTask;
        }
    }
}
