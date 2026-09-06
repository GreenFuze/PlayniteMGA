using System;
using System.Collections.Generic;

namespace MGA.Playnite.Tests
{
    /// <summary>
    /// A minimal test runner.
    ///
    /// net462 with a WPF-referencing library under test makes the usual test
    /// runners more trouble than they are worth here, and the existing Playnite
    /// plugins in this account already run their tests as a console exe. Keeping
    /// the same shape means one way to run tests across all of them.
    /// </summary>
    internal static class Harness
    {
        private static readonly List<string> Failures = new List<string>();
        private static int passed;

        public static void Test(string name, Action body)
        {
            try
            {
                body();
                passed++;
                Console.WriteLine("  ok   " + name);
            }
            catch (Exception ex)
            {
                Failures.Add(name + ": " + ex.Message);
                Console.WriteLine("  FAIL " + name);
                Console.WriteLine("       " + ex.Message);
            }
        }

        public static int Summarize()
        {
            Console.WriteLine();
            Console.WriteLine(passed + " passed, " + Failures.Count + " failed");
            if (Failures.Count == 0)
            {
                return 0;
            }

            Console.WriteLine();
            foreach (var failure in Failures)
            {
                Console.WriteLine("FAILED " + failure);
            }
            return 1;
        }

        public static void AssertTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }

        public static void AssertEqual(object expected, object actual, string message)
        {
            if (!Equals(expected, actual))
            {
                throw new Exception(message + " (expected " + Describe(expected) + ", got " + Describe(actual) + ")");
            }
        }

        public static void AssertNull(object value, string message)
        {
            if (value != null)
            {
                throw new Exception(message + " (got " + Describe(value) + ")");
            }
        }

        public static void AssertNotNull(object value, string message)
        {
            if (value == null)
            {
                throw new Exception(message);
            }
        }

        private static string Describe(object value)
        {
            return value == null ? "null" : "'" + value + "'";
        }
    }
}
