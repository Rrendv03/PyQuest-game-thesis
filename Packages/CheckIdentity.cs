using System;
using System.Reflection;
class CheckIdentity { static void Main(string[] a){ var an=AssemblyName.GetAssemblyName(a[0]); Console.WriteLine(an.FullName); } }
