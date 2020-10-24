using System;

namespace HSPI_WX200MultiStatus
{
    class Program
    {
        static void Main(string[] args)
        {
            HSPI plugin = new HSPI();
            plugin.Connect(args);
        }
    }
}