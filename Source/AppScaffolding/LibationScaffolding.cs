﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ApplicationServices;
using AudibleUtilities;
using Dinah.Core;
using Dinah.Core.IO;
using Dinah.Core.Logging;
using LibationFileManager;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using Serilog;

namespace AppScaffolding
{
	public enum ReleaseIdentifier
	{
		None,
		WindowsClassic = OS.Windows | Variety.Classic | Architecture.X64,
		WindowsAvalonia = OS.Windows | Variety.Chardonnay | Architecture.X64,
		LinuxAvalonia = OS.Linux | Variety.Chardonnay | Architecture.X64,
		MacOSAvalonia = OS.MacOS | Variety.Chardonnay | Architecture.X64,
		LinuxAvalonia_Arm64 = OS.Linux | Variety.Chardonnay | Architecture.Arm64,
		MacOSAvalonia_Arm64 = OS.MacOS | Variety.Chardonnay | Architecture.Arm64
	}

	// I know I'm taking the wine metaphor a bit far by naming this "Variety", but I don't know what else to call it
	[Flags]
	public enum Variety
	{
		None,
		Classic = 0x10000,
		Chardonnay = 0x20000,
	}

	public static class LibationScaffolding
	{
		public const string RepositoryUrl = "ht" + "tps://github.com/rmcrackan/Libation";
		public const string WebsiteUrl = "ht" + "tps://getlibation.com";
		public const string RepositoryLatestUrl = "ht" + "tps://github.com/rmcrackan/Libation/releases/latest";
		public static ReleaseIdentifier ReleaseIdentifier { get; private set; }
		public static Variety Variety { get; private set; }

		public static void SetReleaseIdentifier(Variety varietyType)
		{
			Variety = Enum.IsDefined(varietyType) ? varietyType : Variety.None;

			var releaseID = (ReleaseIdentifier)((int)varietyType | (int)Configuration.OS | (int)RuntimeInformation.ProcessArchitecture);

			if (Enum.IsDefined(releaseID))
				ReleaseIdentifier = releaseID;
			else
			{
				ReleaseIdentifier = ReleaseIdentifier.None;
				Serilog.Log.Logger.Warning("Unknown release identifier @{DebugInfo}", new { Variety = varietyType, Configuration.OS, RuntimeInformation.ProcessArchitecture });
			}
		}

		// AppScaffolding
		private static Assembly _executingAssembly;
		private static Assembly ExecutingAssembly
			=> _executingAssembly ??= Assembly.GetExecutingAssembly();

		// LibationWinForms or LibationCli
		private static Assembly _entryAssembly;
		private static Assembly EntryAssembly
			=> _entryAssembly ??= Assembly.GetEntryAssembly();

		// previously: System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
		private static Version _buildVersion;
		public static Version BuildVersion
			=> _buildVersion
			??= new[] { ExecutingAssembly.GetName(), EntryAssembly.GetName() }
			.Max(a => a.Version);

		/// <summary>Run migrations before loading Configuration for the first time. Then load and return Configuration</summary>
		public static Configuration RunPreConfigMigrations()
		{
			// must occur before access to Configuration instance
			// // outdated. kept here as an example of what belongs in this area
			// // Migrations.migrate_to_v5_2_0__pre_config();

			Configuration.SetLibationVersion(BuildVersion);

			//***********************************************//
			//                                               //
			//   do not use Configuration before this line   //
			//                                               //
			//***********************************************//
			return Configuration.Instance;
		}

		/// <summary>most migrations go in here</summary>
		public static void RunPostConfigMigrations(Configuration config)
		{
			AudibleApiStorage.EnsureAccountsSettingsFileExists();

			//
			// migrations go below here
			//

			Migrations.migrate_to_v6_6_9(config);
		}

		/// <summary>Initialize logging. Wire-up events. Run after migration</summary>
		public static void RunPostMigrationScaffolding(Configuration config)
		{
			ensureSerilogConfig(config);
			configureLogging(config);
			logStartupState(config);

			// all else should occur after logging

			wireUpSystemEvents(config);
		}

		private static void ensureSerilogConfig(Configuration config)
		{
			if (config.GetObject("Serilog") is JObject serilog)
			{
				if (serilog["WriteTo"] is JArray sinks && sinks.FirstOrDefault(s => s["Name"].Value<string>() is "File") is JToken fileSink)
				{
					fileSink["Name"] = "ZipFile";
					config.SetNonString(serilog.DeepClone(), "Serilog");
				}
				return;
			}

			var serilogObj = new JObject
			{
				{ "MinimumLevel", "Information" },
				{ "WriteTo", new JArray
					{
						// new JObject { {"Name", "Console" } }, // this has caused more problems than it's solved
						new JObject
						{
							{ "Name", "ZipFile" },
							{ "Args",
								new JObject
								{
									// for this sink to work, a path must be provided. we override this below
									{ "path", Path.Combine(config.LibationFiles, "_Log.log") },
									{ "rollingInterval", "Month" },
									// Serilog template formatting examples
									// - default:                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
									//   output example:             2019-11-26 08:48:40.224 -05:00 [DBG] Begin Libation
									// - with class and method info: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] (at {Caller}) {Message:lj}{NewLine}{Exception}";
									//   output example:             2019-11-26 08:48:40.224 -05:00 [DBG] (at LibationWinForms.Program.init()) Begin Libation
									// {Properties:j} needed for expanded exception logging
									{ "outputTemplate", "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] (at {Caller}) {Message:lj}{NewLine}{Exception} {Properties:j}" }
								}
							}
						}
					}
				},
				// better exception logging with: Serilog.Exceptions library -- WithExceptionDetails
				{ "Using", new JArray{ "Dinah.Core", "Serilog.Exceptions" } }, // dll's name, NOT namespace
				{ "Enrich", new JArray{ "WithCaller", "WithExceptionDetails" } },
			};
			config.SetNonString(serilogObj, "Serilog");
		}

		// to restore original: Console.SetOut(origOut);
		private static TextWriter origOut { get; } = Console.Out;

		private static void configureLogging(Configuration config)
		{
			config.ConfigureLogging();

			// capture most Console.WriteLine() and write to serilog. See below tests for details.
			// Some dependencies print helpful info via Console.WriteLine. We'd like to log it.
			//
			// If Serilog also writes to Console, this might be asking for trouble. ie: infinite loops.
			// To use that way, SerilogTextWriter needs to be more robust and tested. Esp the Write() methods.
			// However, empirical testing so far has shown no issues.
			Console.SetOut(new MultiTextWriter(origOut, new SerilogTextWriter()));

            #region Console => Serilog tests
            /*
			// all below apply to "Console." and "Console.Out."

			// captured
			Console.WriteLine("str");
			Console.WriteLine(new { a = "anon" });
			Console.WriteLine("{0}", "format");
			Console.WriteLine("{0}{1}", "zero|", "one");
			Console.WriteLine("{0}{1}{2}", "zero|", "one|", "two");
			Console.WriteLine("{0}", new object[] { "arr" });

			// not captured
			Console.WriteLine();
			Console.WriteLine(true);
			Console.WriteLine('0');
			Console.WriteLine(1);
			Console.WriteLine(2m);
			Console.WriteLine(3f);
			Console.WriteLine(4d);
			Console.WriteLine(5L);
			Console.WriteLine((uint)6);
			Console.WriteLine((ulong)7);

			Console.Write("str");
			Console.Write(true);
			Console.Write('0');
			Console.Write(1);
			Console.Write(2m);
			Console.Write(3f);
			Console.Write(4d);
			Console.Write(5L);
			Console.Write((uint)6);
			Console.Write((ulong)7);
			Console.Write(new { a = "anon" });
			Console.Write("{0}", "format");
			Console.Write("{0}{1}", "zero|", "one");
			Console.Write("{0}{1}{2}", "zero|", "one|", "two");
			Console.Write("{0}", new object[] { "arr" });
			*/
            #endregion

            // .Here() captures debug info via System.Runtime.CompilerServices attributes. Warning: expensive
            //var withLineNumbers_outputTemplate = "[{Timestamp:HH:mm:ss} {Level}] {SourceContext}{NewLine}{Message}{NewLine}in method {MemberName} at {FilePath}:{LineNumber}{NewLine}{Exception}{NewLine}";
            //Log.Logger.Here().Debug("Begin Libation. Debug with line numbers");
        }

		private static void logStartupState(Configuration config)
		{
#if DEBUG
			var mode = "Debug";
#else
			var mode = "Release";
#endif
			if (System.Diagnostics.Debugger.IsAttached)
				mode += " (Debugger attached)";

			// begin logging session with a form feed
			Log.Logger.Information("\r\n\f");
			Log.Logger.Information("Begin. {@DebugInfo}", new
			{
				AppName = EntryAssembly.GetName().Name,
				Version = BuildVersion.ToString(),
				ReleaseIdentifier,
				Configuration.OS,
                InteropFactory.InteropFunctionsType,
                Mode = mode,
				LogLevel_Verbose_Enabled = Log.Logger.IsVerboseEnabled(),
				LogLevel_Debug_Enabled = Log.Logger.IsDebugEnabled(),
				LogLevel_Information_Enabled = Log.Logger.IsInformationEnabled(),
				LogLevel_Warning_Enabled = Log.Logger.IsWarningEnabled(),
				LogLevel_Error_Enabled = Log.Logger.IsErrorEnabled(),
				LogLevel_Fatal_Enabled = Log.Logger.IsFatalEnabled(),

                config.BetaOptIn,
                config.UseCoverAsFolderIcon,
                config.LibationFiles,
				AudibleFileStorage.BooksDirectory,

				config.InProgress,

				AudibleFileStorage.DownloadsInProgressDirectory,
				DownloadsInProgressFiles = FileManager.FileUtility.SaferEnumerateFiles(AudibleFileStorage.DownloadsInProgressDirectory).Count(),

				AudibleFileStorage.DecryptInProgressDirectory,
				DecryptInProgressFiles = FileManager.FileUtility.SaferEnumerateFiles(AudibleFileStorage.DecryptInProgressDirectory).Count(),
			});

            if (InteropFactory.InteropFunctionsType is null)
                Serilog.Log.Logger.Warning("WARNING: OSInteropProxy.InteropFunctionsType is null");
        }

        private static void wireUpSystemEvents(Configuration configuration)
		{
			LibraryCommands.LibrarySizeChanged += (_, __) => SearchEngineCommands.FullReIndex();
			LibraryCommands.BookUserDefinedItemCommitted += (_, books) => SearchEngineCommands.UpdateBooks(books);
		}

		public static UpgradeProperties GetLatestRelease()
		{
			// timed out
			(var latest, var zip) = getLatestRelease(TimeSpan.FromSeconds(10));

			if (latest is null || zip is null)
				return null;

			var latestVersionString = latest.TagName.Trim('v');
			if (!Version.TryParse(latestVersionString, out var latestRelease))
				return null;

			// we're up to date
			if (latestRelease <= BuildVersion)
				return null;

			// we have an update

			var zipUrl = zip?.BrowserDownloadUrl;

			Log.Logger.Information("Update available: {@DebugInfo}", new
			{
				latestRelease = latestRelease.ToString(),
				latest.HtmlUrl,
				zipUrl
			});

			return new(zipUrl, latest.HtmlUrl, zip.Name, latestRelease, latest.Body);
		}
		private static (Octokit.Release, Octokit.ReleaseAsset) getLatestRelease(TimeSpan timeout)
		{
			try
			{
				var task = getLatestRelease();
				if (task.Wait(timeout))
					return task.Result;

				Log.Logger.Information("Timed out");
			}
			catch (AggregateException aggEx)
			{
				Log.Logger.Error(aggEx, "Checking for new version too often");
			}
			return (null, null);
		}
		private static async System.Threading.Tasks.Task<(Octokit.Release, Octokit.ReleaseAsset)> getLatestRelease()
		{
			const string ownerAccount = "rmcrackan";
			const string repoName = "Libation";

			var gitHubClient = new Octokit.GitHubClient(new Octokit.ProductHeaderValue(repoName));

			//Download the release index
			var bts = await gitHubClient.Repository.Content.GetRawContent(ownerAccount, repoName, ".releaseindex.json");
			var releaseIndex = JObject.Parse(System.Text.Encoding.ASCII.GetString(bts));

			string regexPattern;

			try
			{
				regexPattern = releaseIndex.Value<string>(InteropFactory.Create().ReleaseIdString);
			}
			catch
			{
				regexPattern = releaseIndex.Value<string>(ReleaseIdentifier.ToString());
			}

			var regex = new System.Text.RegularExpressions.Regex(regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

			//https://docs.github.com/en/rest/releases/releases?apiVersion=2022-11-28#get-the-latest-release
			var latestRelease = await gitHubClient.Repository.Release.GetLatest(ownerAccount, repoName);

			return (latestRelease, latestRelease?.Assets?.FirstOrDefault(a => regex.IsMatch(a.Name)));
		}
	}

	internal static class Migrations
	{
		public static void migrate_to_v6_6_9(Configuration config)
		{
			var writeToPath = $"Serilog.WriteTo";

			// remove WriteTo[].Name == Console
			{
				if (UNSAFE_MigrationHelper.Settings_TryGetArrayLength(writeToPath, out var length1))
				{
					for (var i = length1 - 1; i >= 0; i--)
					{
						var exists = UNSAFE_MigrationHelper.Settings_TryGetFromJsonPath($"{writeToPath}[{i}].Name", out var value);

						if (exists && value == "Console")
							UNSAFE_MigrationHelper.Settings_RemoveFromArray(writeToPath, i);
					}
				}
			}

			// add Serilog.Exceptions -- WithExceptionDetails
			{
				// outputTemplate should contain "{Properties:j}"
				{
					// re-calculate. previous loop may have changed the length
					if (UNSAFE_MigrationHelper.Settings_TryGetArrayLength(writeToPath, out var length2))
					{
						var propertyName = "outputTemplate";
						for (var i = 0; i < length2; i++)
						{
							var jsonPath = $"{writeToPath}[{i}].Args";
							var exists = UNSAFE_MigrationHelper.Settings_TryGetFromJsonPath($"{jsonPath}.{propertyName}", out var value);

							var newValue = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] (at {Caller}) {Message:lj}{NewLine}{Exception} {Properties:j}";

							if (exists && value != newValue)
								UNSAFE_MigrationHelper.Settings_SetWithJsonPath(jsonPath, propertyName, newValue);
						}
					}
				}

				// Serilog.Using must include "Serilog.Exceptions"
				UNSAFE_MigrationHelper.Settings_AddUniqueToArray("Serilog.Using", "Serilog.Exceptions");

				// Serilog.Enrich must include "WithExceptionDetails"
				UNSAFE_MigrationHelper.Settings_AddUniqueToArray("Serilog.Enrich", "WithExceptionDetails");
			}
		}
	}
}
