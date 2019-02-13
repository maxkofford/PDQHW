using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileLocker
{
    class Program
    {
        static void Main(string[] args)
        {
            using (FileStream s2 = new FileStream(args[0], FileMode.Open, FileAccess.Read, FileShare.None))
            {
            
                Console.ReadLine();
            }

        }
    }
}
