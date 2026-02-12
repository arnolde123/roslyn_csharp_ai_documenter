using System;
using System.Collections.Generic;

namespace ExampleApp
{
        /// <summary>
    /// Provides functionality to manage a collection of users, including adding, removing, and retrieving user information.
    /// </summary>
    public class UserService
    {
        private List<string> users;

        public UserService()
        {
            users = new List<string>();
        }

                /// <summary>
        /// Adds a new user to the system with the specified username and email.
        /// </summary>
        /// <param name="username">The username of the user to be added.</param>
        /// <param name="email">The email address of the user to be added.</param>
        /// <returns>True if the user was added successfully; otherwise, false.</returns>
public bool AddUser(string username, string email)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(email))
            {
                return false;
            }

            if (users.Contains(username))
            {
                return false;
            }

            users.Add(username);
            return true;
        }

                /// <summary>
        /// Retrieves the total number of users in the system.
        /// </summary>
        /// <returns>
        /// The count of users as an integer.
        /// </returns>
public int GetUserCount()
        {
            return users.Count;
        }

                /// <summary>
        /// Retrieves a list of all users in the system.
        /// </summary>
        /// <returns>
        /// A list of strings representing the usernames of all users.
        /// </returns>
public List<string> GetAllUsers()
        {
            return new List<string>(users);
        }

                /// <summary>
        /// Removes a user from the user service by their username.
        /// </summary>
        /// <param name="username">The username of the user to be removed.</param>
        /// <returns>True if the user was successfully removed; otherwise, false.</returns>
public bool RemoveUser(string username)
        {
            return users.Remove(username);
        }

                /// <summary>
        /// Clears all users from the user service.
        /// </summary>
        /// <param name="users">The collection of users to be cleared.</param>
        /// <returns>None.</returns>
public void ClearAllUsers()
        {
            users.Clear();
        }
    }

        public class Calculator
    {
                /// <summary>
        /// Adds two double-precision floating-point numbers.
        /// </summary>
        /// <param name="a">The first number to add.</param>
        /// <param name="b">The second number to add.</param>
        /// <returns>The sum of the two numbers.</returns>
public double Add(double a, double b)
        {
            return a + b;
        }

                /// <summary>
        /// Subtracts the second double value from the first double value.
        /// </summary>
        /// <param name="a">The first double value from which to subtract.</param>
        /// <param name="b">The second double value to subtract.</param>
        /// <returns>The result of subtracting <paramref name="b"/> from <paramref name="a"/>.</returns>
public double Subtract(double a, double b)
        {
            return a - b;
        }

                /// <summary>
        /// Multiplies two double-precision floating-point numbers.
        /// </summary>
        /// <param name="a">The first number to multiply.</param>
        /// <param name="b">The second number to multiply.</param>
        /// <returns>The product of the two numbers.</returns>
public double Multiply(double a, double b)
        {
            return a * b;
        }

                /// <summary>
        /// Divides two double-precision floating-point numbers.
        /// </summary>
        /// <param name="a">The dividend, a double value to be divided.</param>
        /// <param name="b">The divisor, a double value by which to divide the dividend. Must not be zero.</param>
        /// <returns>
        /// The result of dividing <paramref name="a"/> by <paramref name="b"/>.
        /// </returns>
        /// <exception cref="DivideByZeroException">
        /// Thrown when <paramref name="b"/> is zero.
        /// </exception>
public double Divide(double a, double b)
        {
            if (b == 0)
            {
                throw new DivideByZeroException("Cannot divide by zero");
            }
            return a / b;
        }

                /// <summary>
        /// Calculates the result of raising a specified base value to a specified exponent.
        /// </summary>
        /// <param name="baseValue">The base value to be raised.</param>
        /// <param name="exponent">The exponent to which the base value is raised.</param>
        /// <returns>The result of the base value raised to the exponent.</returns>
public double Power(double baseValue, double exponent)
        {
            return Math.Pow(baseValue, exponent);
        }
    }

        public class DataProcessor
    {
                /// <summary>
        /// Processes the input string based on the specified flag.
        /// </summary>
        /// <param name="input">The input string to be processed.</param>
        /// <param name="uppercase">A boolean value indicating whether to convert the input string to uppercase.</param>
        /// <returns>A processed string, either in uppercase or lowercase based on the value of the uppercase parameter.</returns>
public string ProcessData(string input, bool uppercase)
        {
            if (uppercase)
            {
                return input.ToUpper();
            }
            return input.ToLower();
        }

                /// <summary>
        /// Filters the given array of integers, returning only those that are greater than the specified threshold.
        /// </summary>
        /// <param name="numbers">An array of integers to be filtered.</param>
        /// <param name="threshold">An integer threshold value; only numbers greater than this value will be included in the result.</param>
        /// <returns>An array of integers that are greater than the specified threshold.</returns>
public int[] FilterNumbers(int[] numbers, int threshold)
        {
            var result = new List<int>();
            foreach (var num in numbers)
            {
                if (num > threshold)
                {
                    result.Add(num);
                }
            }
            return result.ToArray();
        }

                /// <summary>
        /// Counts the occurrences of each word in the provided text.
        /// </summary>
        /// <param name="text">The input string containing the text to be processed.</param>
        /// <returns>A dictionary where the keys are words and the values are the counts of those words in the input text.</returns>
public Dictionary<string, int> CountWords(string text)
        {
            var words = text.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var counts = new Dictionary<string, int>();

            foreach (var word in words)
            {
                if (counts.ContainsKey(word))
                {
                    counts[word]++;
                }
                else
                {
                    counts[word] = 1;
                }
            }

            return counts;
        }
    }
}

