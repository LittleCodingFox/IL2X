using System;
using System.IO;
using System.Linq;
using IL2X.Core;
using IL2X.Core.Emitters;

namespace IL2X.CLI
{
	class Program
	{
		static void Main(string[] args)
		{
			string projPath = null;
			string outPath = null;
			var solutionType = Solution.Type.Executable;
			bool optimize = false;

			if(args.Length == 0 || args[1] == "--help")
			{
				Console.WriteLine("IL2X Options");
                Console.WriteLine("\t-i: Input assembly path");
                Console.WriteLine("\t-o: Output source directory path");
                Console.WriteLine($"\t-t: Assembly type to process ({string.Join(", ", Enum.GetValues<Solution.Type>().Select(x => x.ToString().ToLowerInvariant()))})");
                Console.WriteLine("\t-O0: Don't optimize");
                Console.WriteLine("\t-O1: Optimize");

                return;
			}

			for(var i = 0; i < args.Length; i++)
			{
				switch(args[i])
				{
					case "-i":

						if(i + 1 < args.Length)
						{
							projPath = args[i + 1];

							i++;
						}

						break;

					case "-o":

                        if (i + 1 < args.Length)
                        {
                            outPath = args[i + 1];

                            i++;
                        }

                        break;

					case "-O0":

						optimize = false;

						break;

					case "-O1":

						optimize = true;

						break;

					case "-t":

                        if (i + 1 < args.Length)
                        {
                            if(Enum.TryParse(args[i + 1], true, out solutionType) == false)
							{
								Console.WriteLine($"Invalid solution type: {solutionType}.\nExpected Values:\n");

								foreach(var value in Enum.GetValues<Solution.Type>())
								{
									Console.WriteLine($"- {value.ToString().ToLowerInvariant()}");
								}

								Environment.Exit(1);
							}

                            i++;
                        }

                        break;
				}
			}

            try
            {
                if(File.Exists(projPath) == false)
				{
					Console.WriteLine($"Assembly not found at {projPath}");

					Environment.Exit(1);
				}
            }
            catch (Exception)
            {
                Console.WriteLine($"Assembly not found at {projPath}");

                Environment.Exit(1);
            }

			if(outPath == null)
			{
				Console.WriteLine($"Output path not set, aborting...");

				Environment.Exit(1);
			}

			try
			{
				Directory.CreateDirectory(outPath);
			}
			catch(Exception)
			{
			}

            using (var solution = new Solution(solutionType, projPath))
			{
				solution.ReLoad();
				solution.Jit();

				if(optimize)
				{
                    solution.Optimize();
                }

				var emitter = new Emmiter_C(solution);

				emitter.Translate(Path.Combine(outPath, "IL2XOutput"));
			}
		}
	}
}
