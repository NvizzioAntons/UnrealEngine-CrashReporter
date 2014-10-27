﻿// Copyright 1998-2014 Epic Games, Inc. All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace UnrealBuildTool.Android
{
	public class UEDeployAndroid : UEBuildDeploy
	{
		/**
		 *	Register the platform with the UEBuildDeploy class
		 */
		public override void RegisterBuildDeploy()
		{
			string NDKPath = Environment.GetEnvironmentVariable("ANDROID_HOME");

			// we don't have an NDKROOT specified
			if (String.IsNullOrEmpty(NDKPath))
			{
				Log.TraceVerbose("        Unable to register Android deployment class because the ANDROID_HOME environment variable isn't set or points to something that doesn't exist");
			}
			else
			{
				UEBuildDeploy.RegisterBuildDeploy(UnrealTargetPlatform.Android, this);
			}
		}

		/** Internal usage for GetApiLevel */
		private static List<string> PossibleApiLevels = null;

		/** Simple function to pipe output asynchronously */
		private static void ParseApiLevel(object Sender, DataReceivedEventArgs Event)
		{
			// DataReceivedEventHandler is fired with a null string when the output stream is closed.  We don't want to
			// print anything for that event.
			if (!String.IsNullOrEmpty(Event.Data))
			{
				string Line = Event.Data;
				if (Line.StartsWith("id:"))
				{
					// the line should look like: id: 1 or "android-19"
					string[] Tokens = Line.Split("\"".ToCharArray());
					if (Tokens.Length >= 2)
					{
						PossibleApiLevels.Add(Tokens[1]);
					}
				}
			}
		}

		static private string CachedSDKLevel = null;
		private static string GetSdkApiLevel()
		{
			if (CachedSDKLevel == null)
			{
				// default to looking on disk for latest API level
				string Target = AndroidPlatform.AndroidSdkApiTarget;

				// if we want to use whatever version the ndk uses, then use that
				if (Target == "matchndk")
				{
					Target = AndroidToolChain.GetNdkApiLevel();
				}

				// run a command and capture output
				if (Target == "latest")
				{
					// we expect there to be one, so use the first one
					string AndroidCommandPath = Environment.ExpandEnvironmentVariables("%ANDROID_HOME%/tools/android.bat");

					var ExeInfo = new ProcessStartInfo(AndroidCommandPath, "list targets");
					ExeInfo.UseShellExecute = false;
					ExeInfo.RedirectStandardOutput = true;
					using (var GameProcess = Process.Start(ExeInfo))
					{
						PossibleApiLevels = new List<string>();
						GameProcess.BeginOutputReadLine();
						GameProcess.OutputDataReceived += ParseApiLevel;
						GameProcess.WaitForExit();
					}

					if (PossibleApiLevels != null && PossibleApiLevels.Count > 0)
					{
						Target = AndroidToolChain.GetLargestApiLevel(PossibleApiLevels.ToArray());
					}
					else
					{
						throw new BuildException("Can't make an APK an API installed (see \"android.bat list targets\")");
					}
				}

				Console.WriteLine("Building with SDK API '{0}'", Target);
				CachedSDKLevel = Target;
			}

			return CachedSDKLevel;
		}

		private static string GetAntPath()
		{
			// look up an ANT_HOME env var
			string AntHome = Environment.GetEnvironmentVariable("ANT_HOME");
			if (!string.IsNullOrEmpty(AntHome) && Directory.Exists(AntHome))
			{
				string AntPath = AntHome + "/bin/ant.bat";
				// use it if found
				if (File.Exists(AntPath))
				{
					return AntPath;
				}
			}

			// otherwise, look in the eclipse install for the ant plugin (matches the unzipped Android ADT from Google)
			string PluginDir = Environment.ExpandEnvironmentVariables("%ANDROID_HOME%/../eclipse/plugins");
			if (Directory.Exists(PluginDir))
			{
				string[] Plugins = Directory.GetDirectories(PluginDir, "org.apache.ant*");
				// use the first one with ant.bat
				if (Plugins.Length > 0)
				{
					foreach (string PluginName in Plugins)
					{
						string AntPath = PluginName + "/bin/ant.bat";
						// use it if found
						if (File.Exists(AntPath))
						{
							return AntPath;
						}
					}
				}
			}

			throw new BuildException("Unable to find ant.bat (via %ANT_HOME% or %ANDROID_HOME%/../eclipse/plugins/org.apache.ant*");
		}


		private static void CopyFileDirectory(string SourceDir, string DestDir, Dictionary<string,string> Replacements)
		{
			if (!Directory.Exists(SourceDir))
			{
				return;
			}

			string[] Files = Directory.GetFiles(SourceDir, "*.*", SearchOption.AllDirectories);
			foreach (string Filename in Files)
			{
				// skip template files
				if (Path.GetExtension(Filename) == ".template")
				{
					continue;
				}

				// make the dst filename with the same structure as it was in SourceDir
				string DestFilename = Path.Combine(DestDir, Utils.MakePathRelativeTo(Filename, SourceDir));
				if (File.Exists(DestFilename))
				{
					File.Delete(DestFilename);
				}

				// make the subdirectory if needed
				string DestSubdir = Path.GetDirectoryName(DestFilename);
				if (!Directory.Exists(DestSubdir))
				{
					Directory.CreateDirectory(DestSubdir);
				}

				// some files are handled specially
				string Ext = Path.GetExtension(Filename);
				if (Ext == ".xml")
				{
					string Contents = File.ReadAllText(Filename);

					// replace some varaibles
					foreach (var Pair in Replacements)
					{
						Contents = Contents.Replace(Pair.Key, Pair.Value);
					}

					// write out file
					File.WriteAllText(DestFilename, Contents);
				}
				else
				{
					File.Copy(Filename, DestFilename);

					// remove any read only flags
					FileInfo DestFileInfo = new FileInfo(DestFilename);
					DestFileInfo.Attributes = DestFileInfo.Attributes & ~FileAttributes.ReadOnly;
				}
			}
		}

		private static void DeleteDirectory(string InPath, string SubDirectoryToKeep="")
		{
			// skip the dir we want to
			if (String.Compare(Path.GetFileName(InPath), SubDirectoryToKeep, true) == 0)
			{
				return;
			}
			
			// delete all files in here
			string[] Files = Directory.GetFiles(InPath);
			foreach (string Filename in Files)
			{
				try
				{
					// remove any read only flags
					FileInfo FileInfo = new FileInfo(Filename);
					FileInfo.Attributes = FileInfo.Attributes & ~FileAttributes.ReadOnly;
					FileInfo.Delete();
				}
				catch (Exception)
				{
					Log.TraceInformation("Failed to delete all files in directory {0}. Continuing on...", InPath);
				}
			}

			string[] Dirs = Directory.GetDirectories(InPath, "*.*", SearchOption.TopDirectoryOnly);
			foreach (string Dir in Dirs)
			{
				DeleteDirectory(Dir, SubDirectoryToKeep);
				// try to delete the directory, but allow it to fail (due to SubDirectoryToKeep still existing)
				try
				{
					Directory.Delete(Dir);
				}
				catch (Exception)
				{
					// do nothing
				}
			}
		}

        public string GetUE4BuildFilePath(String EngineDirectory)
        {
            return Path.GetFullPath(Path.Combine(EngineDirectory, "Build/Android/Java"));
        }

        public string GetUE4JavaFilePath(String EngineDirectory)
        {
            return Path.GetFullPath(Path.Combine(GetUE4BuildFilePath(EngineDirectory), "src/com/epicgames/ue4"));
        }

        public string GetUE4JavaBuildSettingsFileName(String EngineDirectory)
        {
            return Path.Combine(GetUE4JavaFilePath(EngineDirectory), "JavaBuildSettings.java");
        }

        public void WriteJavaBuildSettingsFile(string FileName, bool OBBinAPK)
        {
             // (!UEBuildConfiguration.bOBBinAPK ? "PackageType.AMAZON" : /*bPackageForGoogle ? "PackageType.GOOGLE" :*/ "PackageType.DEVELOPMENT") + ";\n");
            string Setting = OBBinAPK ? "AMAZON" : "DEVELOPMENT";
            if (!File.Exists(FileName) || ShouldWriteJavaBuildSettingsFile(FileName, Setting))
            {
				Log.TraceInformation("{0} ====WRITING JAVABUILDSETTINGS.JAVA========================================================", DateTime.Now.ToString());
				StringBuilder BuildSettings = new StringBuilder("package com.epicgames.ue4;\npublic class JavaBuildSettings\n{\n");
                BuildSettings.Append("\tpublic enum PackageType {AMAZON, GOOGLE, DEVELOPMENT};\n");
                BuildSettings.Append("\tpublic static final PackageType PACKAGING = PackageType." + Setting + ";\n");
                BuildSettings.Append("}\n");
                File.WriteAllText(FileName, BuildSettings.ToString());
            }
        }

        public bool ShouldWriteJavaBuildSettingsFile(string FileName, string setting)
        {
            var fileContent = File.ReadAllLines(FileName);
            var packageLine = fileContent[4]; // We know this to be true... because we write it below...
            int location = packageLine.IndexOf("PACKAGING") + 12 + 12; // + (PACKAGING = ) + ("PackageType.")
            return String.Compare(setting, packageLine.Substring(location, setting.Length)) != 0;
        }

		private static string GetNDKArch(string UE4Arch)
		{
			switch (UE4Arch)
			{
				case "-armv7": return "armeabi-v7a";
				case "-x86": return "x86";

				default: throw new BuildException("Unknown UE4 architecture {0}", UE4Arch);
			}
		}

		public static string GetUE4Arch(string NDKArch)
		{
			switch (NDKArch)
			{
				case "armeabi-v7a": return "-armv7";
				case "x86":			return "-x86";

				default: throw new BuildException("Unknown NDK architecture '{0}'", NDKArch);
			}
		}

		private static void CopySTL(string UE4BuildPath, string UE4Arch)
		{
			string Arch = GetNDKArch(UE4Arch);

			string GccVersion = "4.8";
			if (!Directory.Exists(Environment.ExpandEnvironmentVariables("%NDKROOT%/sources/cxx-stl/gnu-libstdc++/4.8")))
			{
				GccVersion = "4.6";
			}

			// copy it in!
			string SourceSTLSOName = Environment.ExpandEnvironmentVariables("%NDKROOT%/sources/cxx-stl/gnu-libstdc++/") + GccVersion + "/libs/" + Arch + "/libgnustl_shared.so";
			string FinalSTLSOName = UE4BuildPath + "/libs/" + Arch + "/libgnustl_shared.so";
			Directory.CreateDirectory(Path.GetDirectoryName(FinalSTLSOName));
			File.Copy(SourceSTLSOName, FinalSTLSOName);
		}

		//@TODO: To enable the NVIDIA Gfx Debugger
		private static void CopyGfxDebugger(string UE4BuildPath, string UE4Arch)
		{
//			string Arch = GetNDKArch(UE4Arch);
//			Directory.CreateDirectory(UE4BuildPath + "/libs/" + Arch);
//			File.Copy("E:/Dev2/UE4-Clean/Engine/Source/ThirdParty/NVIDIA/TegraGfxDebugger/libNvPmApi.Core.so", UE4BuildPath + "/libs/" + Arch + "/libNvPmApi.Core.so");
//			File.Copy("E:/Dev2/UE4-Clean/Engine/Source/ThirdParty/NVIDIA/TegraGfxDebugger/libTegra_gfx_debugger.so", UE4BuildPath + "/libs/" + Arch + "/libTegra_gfx_debugger.so");
		}

		private static void RunCommandLineProgramAndThrowOnError(string WorkingDirectory, string Command, string Params)
		{
			ProcessStartInfo StartInfo = new ProcessStartInfo();
			StartInfo.WorkingDirectory = WorkingDirectory;
			StartInfo.FileName = Command;
			StartInfo.Arguments = Params;
			StartInfo.UseShellExecute = false;
			Log.TraceInformation("\nRunning: " + StartInfo.FileName + " " + StartInfo.Arguments);
			Process Proc = new Process();
			Proc.StartInfo = StartInfo;
			Proc.Start();
			Proc.WaitForExit();

			// android bat failure
			if (Proc.ExitCode != 0)
			{
				throw new BuildException("{0} failed with args {1}", Command, Params);
			}
		}

		private static string UpdateGooglePlayServicesAndReturnLocation(string EngineDirectory, string ProjectDirectory)
		{
			string AndroidCommandPath = Environment.ExpandEnvironmentVariables("%ANDROID_HOME%/tools/android.bat");
			string IntermediateAndroidPath = Path.Combine(ProjectDirectory, "Intermediate/Android/");
			string UE4BuildPath = Path.Combine(IntermediateAndroidPath, "APK");

			// We update into a temporary directory, as changing the API level will modify files
			string GooglePlayServicesSourcePath = Path.GetFullPath(Path.Combine(EngineDirectory, "Build/Android/Java/google-play-services_lib_rev19/"));
			string GooglePlayServicesIntermediatePath = Path.GetFullPath(Path.Combine(IntermediateAndroidPath, "google-play-services_lib/"));

			// are we up to date? (version is current and the "bin" directory exists meaning we ran once)
			bool bIsUpToDate = IsAPIVersionCurrent(GooglePlayServicesIntermediatePath) && Directory.Exists(Path.Combine(GooglePlayServicesIntermediatePath, "bin"));

			if (!bIsUpToDate)
			{
				Log.TraceInformation("{0} ====UPDATING GOOGLE SERVICES LIBRARY======================================================", DateTime.Now.ToString());
				Log.TraceInformation("Google Play Services library will now be updated...");

				// wipe it and start over
				DeleteDirectory(GooglePlayServicesIntermediatePath);
				CopyFileDirectory(GooglePlayServicesSourcePath, GooglePlayServicesIntermediatePath, new Dictionary<string, string>());

				// update the project
				RunCommandLineProgramAndThrowOnError(GooglePlayServicesIntermediatePath, AndroidCommandPath, "update project " + " --path . --target " + GetSdkApiLevel());

				// precompile the lib
				RunCommandLineProgramAndThrowOnError(GooglePlayServicesIntermediatePath, "cmd.exe", "/c \"" + GetAntPath() + "\" release");
			}

			//need to create separate run for each lib. Will be added to project.properties in order in which they are added 
			//the order is important.
			//as android.library.reference.X=libpath where X = 1 - N
			//for e.g this one will be added as android.library.reference.1=<EngineDirectory>/Source/ThirdParty/Android/google_play_services_lib

			// Ant seems to need a relative path to work
			Uri ServicesBuildUri = new Uri(GooglePlayServicesIntermediatePath);
			Uri ProjectUri = new Uri(UE4BuildPath + "/");

			return ProjectUri.MakeRelativeUri(ServicesBuildUri).ToString();
		}

		private static bool IsAPIVersionCurrent(string PropertiesFileLocation)
		{
			string ProjectProperties = Path.Combine(PropertiesFileLocation, "project.properties");
			if (File.Exists(ProjectProperties))
			{
				string[] Contents = File.ReadAllLines(ProjectProperties);
				foreach (string Line in Contents)
				{
					// look for the special line
					if (Line.StartsWith("target="))
					{
						// skip over target=
						string Version = Line.Substring(7);
						// do they match?
						return string.Compare(Version, GetSdkApiLevel(), true) == 0;
					}
				}
			}

			// if any other case happens, we are not current
			return false;
		}

		private void MakeApk(string ProjectName, string ProjectDirectory, string OutputPath, string EngineDirectory, bool bForDistribution, string CookFlavor, bool bMakeSeparateApks, bool bIncrementalPackage)
		{
			// cache some tools paths
			string AndroidCommandPath = Environment.ExpandEnvironmentVariables("%ANDROID_HOME%/tools/android.bat");
			string NDKBuildPath = Environment.ExpandEnvironmentVariables("%NDKROOT%/ndk-build.cmd");
			string AntBuildPath = GetAntPath();

			// set up some directory info
			string IntermediateAndroidPath = Path.Combine(ProjectDirectory, "Intermediate/Android/");
			string UE4BuildPath = Path.Combine(IntermediateAndroidPath, "APK");
			string UE4BuildFilesPath = GetUE4BuildFilePath(EngineDirectory);
			string GameBuildFilesPath = Path.Combine(ProjectDirectory, "Build/Android");
	
			string[] Arches = AndroidToolChain.GetAllArchitectures();
			string[] GPUArchitectures = AndroidToolChain.GetAllGPUArchitectures();
			int NumArches = Arches.Length * GPUArchitectures.Length;

            // See if we need to create a 'default' Java Build settings file if one doesn't exist (if it does exist we have to assume it has been setup correctly)
            string UE4JavaBuildSettingsFileName = GetUE4JavaBuildSettingsFileName(EngineDirectory);
            WriteJavaBuildSettingsFile(UE4JavaBuildSettingsFileName, UEBuildConfiguration.bOBBinAPK);

			// if the last packaged version is wrong, then we can skip the checking and do it
			if (!IsAPIVersionCurrent(UE4BuildPath))
			{
				Log.TraceInformation("Output .apk file(s) were made with a different API version, forcing repackage.");
			}
			else
			{
				// check if so's are up to date against various inputs
				bool bAllInputsCurrent = true;

				foreach (string Arch in Arches)
				{
					foreach (string GPUArch in GPUArchitectures)
					{
						string SourceSOName = AndroidToolChain.InlineArchName(OutputPath, Arch, GPUArch);
						// if the source binary was UE4Game, replace it with the new project name, when re-packaging a binary only build
						string ApkFilename = Path.GetFileNameWithoutExtension(OutputPath).Replace("UE4Game", ProjectName);
						string DestApkName = Path.Combine(ProjectDirectory, "Binaries/Android/") + ApkFilename + ".apk";

						// if we making multiple Apks, we need to put the architecture into the name
						if (bMakeSeparateApks)
						{
							DestApkName = AndroidToolChain.InlineArchName(DestApkName, Arch, GPUArch);
						}

						// check to see if it's out of date before trying the slow make apk process (look at .so and all Engine and Project build files to be safe)
						List<String> InputFiles = new List<string>();
						InputFiles.Add(SourceSOName);
						InputFiles.AddRange(Directory.EnumerateFiles(UE4BuildFilesPath, "*.*", SearchOption.AllDirectories));
						if (Directory.Exists(GameBuildFilesPath))
						{
							InputFiles.AddRange(Directory.EnumerateFiles(GameBuildFilesPath, "*.*", SearchOption.AllDirectories));
						}

						// rebuild if .ini files change
						// @todo android: programmatically determine if any .ini setting changed?
						InputFiles.Add(Path.Combine(EngineDirectory, "Config\\BaseEngine.ini"));
						InputFiles.Add(Path.Combine(ProjectDirectory, "Config\\DefaultEngine.ini"));

						// make sure changed java settings will rebuild apk
						InputFiles.Add(UE4JavaBuildSettingsFileName);

						// rebuild if .pak files exist for OBB in APK case
						if (UEBuildConfiguration.bOBBinAPK)
						{
							string PAKFileLocation = ProjectDirectory + "/Saved/StagedBuilds/Android" + CookFlavor + "/" + ProjectName + "/Content/Paks";
							if (Directory.Exists(PAKFileLocation))
							{
								var PakFiles = Directory.EnumerateFiles(PAKFileLocation, "*.pak", SearchOption.TopDirectoryOnly);
								foreach (var Name in PakFiles)
								{
									InputFiles.Add(Name);
								}
							}
						}

						// look for any newer input file
						DateTime ApkTime = File.GetLastWriteTimeUtc(DestApkName);
						foreach (var InputFileName in InputFiles)
						{
							if (File.Exists(InputFileName))
							{
								// skip .log files
								if (Path.GetExtension(InputFileName) == ".log")
								{
									continue;
								}
								DateTime InputFileTime = File.GetLastWriteTimeUtc(InputFileName);
								if (InputFileTime.CompareTo(ApkTime) > 0)
								{
									bAllInputsCurrent = false;
									Log.TraceInformation("{0} is out of date due to newer input file {1}", DestApkName, InputFileName);
									break;
								}
							}
						}
					}
				}
				if (bAllInputsCurrent)
				{
					Log.TraceInformation("Output .apk file(s) are up to date (compared to the .so and .java input files)");
					return;
				}
			}


			// Once for all arches code:

			// make up a dictionary of strings to replace in the Manifest file
			Dictionary<string, string> Replacements = new Dictionary<string, string>();
			Replacements.Add("${EXECUTABLE_NAME}", ProjectName);

			// distribution apps can't be debuggable, so if it was set to true, set it to false:
			if (bForDistribution)
			{
				Replacements.Add("android:debuggable=\"true\"", "android:debuggable=\"false\"");
			}

			// make sure that GooglePlayServices is up to date, and get the location so we can import it
			// and if we have done a recent, incremental build with the same version, then the intermediate GPS stuff is up to date
			string RelativeGPSLocation = UpdateGooglePlayServicesAndReturnLocation(EngineDirectory, ProjectDirectory);

			Log.TraceInformation("{0} ====PREPARING TO MAKE APK=================================================================", DateTime.Now.ToString());

			if (!bIncrementalPackage)
			{
				// Wipe the Intermediate/Build/APK directory first, except for dexedLibs, because Google Services takes FOREVER to predex, and it never changes
				// so allow the ANT checking to win here - if this grows a bit with extra libs, it's fine, it _should_ only pull in dexedLibs it needs
				Log.TraceInformation("Performing complete package - wiping {0}, except for predexedLibs", UE4BuildPath);
				DeleteDirectory(UE4BuildPath, "dexedLibs");
			}

			// If we are packaging for Amazon then we need to copy the PAK files to the correct location
			// Currently we'll just support 1 of 'em
			if (UEBuildConfiguration.bOBBinAPK)
			{
				string PAKFileLocation = ProjectDirectory + "/Saved/StagedBuilds/Android" + CookFlavor + "/" + ProjectName + "/Content/Paks";
				Console.WriteLine("Pak location {0}", PAKFileLocation);
				string PAKFileDestination = UE4BuildPath + "/assets";
				Console.WriteLine("Pak destination location {0}", PAKFileDestination);
				if (Directory.Exists(PAKFileLocation))
				{
					Directory.CreateDirectory(UE4BuildPath);
					Directory.CreateDirectory(PAKFileDestination);
					Console.WriteLine("PAK file exists...");
					var PakFiles = Directory.EnumerateFiles(PAKFileLocation, "*.pak", SearchOption.TopDirectoryOnly);
					foreach (var Name in PakFiles)
					{
						Console.WriteLine("Found file {0}", Name);
					}

					if (PakFiles.Count() > 0)
					{
						var DestFileName = Path.Combine(PAKFileDestination, Path.GetFileName(PakFiles.ElementAt(0)) + ".png"); // Need a rename to turn off compression
						var SrcFileName = PakFiles.ElementAt(0);
						if (!File.Exists(DestFileName) || File.GetLastWriteTimeUtc(DestFileName) < File.GetLastWriteTimeUtc(SrcFileName))
						{
							Console.WriteLine("Copying {0} to {1}", SrcFileName, DestFileName);
							File.Copy(SrcFileName, DestFileName);
						}
					}
				}
				// Do we want to kill the OBB here or not???
			}

			//Copy build files to the intermediate folder in this order (later overrides earlier):
			//	- Shared Engine
			//  - Shared Engine NoRedist (for Epic secret files)
			//  - Game
			//  - Game NoRedist (for Epic secret files)
			CopyFileDirectory(UE4BuildFilesPath, UE4BuildPath, Replacements);
			CopyFileDirectory(UE4BuildFilesPath + "/NotForLicensees", UE4BuildPath, Replacements);
			CopyFileDirectory(UE4BuildFilesPath + "/NoRedist", UE4BuildPath, Replacements);
			CopyFileDirectory(GameBuildFilesPath, UE4BuildPath, Replacements);
			CopyFileDirectory(GameBuildFilesPath + "/NotForLicensees", UE4BuildPath, Replacements);
			CopyFileDirectory(GameBuildFilesPath + "/NoRedist", UE4BuildPath, Replacements);

			// update metadata files (like project.properties, build.xml) if we are missing a build.xml or if we just overwrote project.properties with a bad version in it (from game/engine dir)
			string BuildXmlFile = Path.Combine(UE4BuildPath, "build.xml");
			if (!File.Exists(BuildXmlFile) || !IsAPIVersionCurrent(UE4BuildPath))
			{
				Log.TraceInformation("{0} ====UPDATING BUILD.XML AND PROJECT.PROPERTIES (MISSING OR BAD SDK VERSION)================", DateTime.Now.ToString());
				RunCommandLineProgramAndThrowOnError(UE4BuildPath, AndroidCommandPath, "update project --name " + ProjectName + " --path . --target " + GetSdkApiLevel() + " --library " + RelativeGPSLocation);
			}

			// now make the apk(s)
			string FinalNdkBuildABICommand = "";
			for (int ArchIndex = 0; ArchIndex < Arches.Length; ArchIndex++)
			{
				string Arch = Arches[ArchIndex];
				for (int GPUArchIndex = 0; GPUArchIndex < GPUArchitectures.Length; GPUArchIndex++)
				{
					Log.TraceInformation("{0} ====PREPARING NATIVE CODE=================================================================", DateTime.Now.ToString());

					string GPUArchitecture = GPUArchitectures[GPUArchIndex];
					string SourceSOName = AndroidToolChain.InlineArchName(OutputPath, Arch, GPUArchitecture);
					// if the source binary was UE4Game, replace it with the new project name, when re-packaging a binary only build
					string ApkFilename = Path.GetFileNameWithoutExtension(OutputPath).Replace("UE4Game", ProjectName);
					string DestApkName = Path.Combine(ProjectDirectory, "Binaries/Android/") + ApkFilename + ".apk";

					// if we making multiple Apks, we need to put the architecture into the name
					if (bMakeSeparateApks)
					{
						DestApkName = AndroidToolChain.InlineArchName(DestApkName, Arch, GPUArchitecture);
					}

					// Copy the generated .so file from the binaries directory to the jni folder
					if (!File.Exists(SourceSOName))
					{
						throw new BuildException("Can't make an APK without the compiled .so [{0}]", SourceSOName);
					}
					if (!Directory.Exists(UE4BuildPath + "/jni"))
					{
						throw new BuildException("Can't make an APK without the jni directory [{0}/jni]", UE4BuildFilesPath);
					}

					// Use ndk-build to do stuff and move the .so file to the lib folder (only if NDK is installed)
					string FinalSOName = "";
					if (File.Exists(NDKBuildPath))
					{
						string LibDir = UE4BuildPath + "/jni/" + GetNDKArch(Arch);
						Directory.CreateDirectory(LibDir);

						// copy the binary to the standard .so location
						FinalSOName = LibDir + "/libUE4.so";
						File.Copy(SourceSOName, FinalSOName, true);

						FinalNdkBuildABICommand += GetNDKArch(Arch) + " ";
					}
					else
					{
						// if no NDK, we don't need any of the debugger stuff, so we just copy the .so to where it will end up
						FinalSOName = UE4BuildPath + "/libs/" + GetNDKArch(Arch) + "/libUE4.so";
						Directory.CreateDirectory(Path.GetDirectoryName(FinalSOName));
						File.Copy(SourceSOName, FinalSOName);
					}

					// now do final stuff per apk (or after all .so's for a shared .apk)
					if (bMakeSeparateApks || ArchIndex == NumArches - 1)
					{
						// always delete libs up to this point so fat binaries and incremental builds work together (otherwise we might end up with multiple
						// so files in an apk that doesn't want them)
						DeleteDirectory(UE4BuildPath + "/libs");

						// if we need to run ndk-build, do it now (if making a shared .apk, we need to wait until all .libs exist)
						if (!string.IsNullOrEmpty(FinalNdkBuildABICommand))
						{
							ProcessStartInfo NDKBuildInfo = new ProcessStartInfo();
							NDKBuildInfo.WorkingDirectory = UE4BuildPath;
							NDKBuildInfo.FileName = NDKBuildPath;
							NDKBuildInfo.Arguments = "APP_ABI=\"" + FinalNdkBuildABICommand + "\"";
							FinalNdkBuildABICommand = "";
							if (!bForDistribution)
							{
								NDKBuildInfo.Arguments += " NDK_DEBUG=1";
							}
							NDKBuildInfo.UseShellExecute = true;
							NDKBuildInfo.WindowStyle = ProcessWindowStyle.Minimized;
							Console.WriteLine("\nRunning: " + NDKBuildInfo.FileName + " " + NDKBuildInfo.Arguments);
							Process NDKBuild = new Process();
							NDKBuild.StartInfo = NDKBuildInfo;
							NDKBuild.Start();
							NDKBuild.WaitForExit();

							// ndk build failure
							if (NDKBuild.ExitCode != 0)
							{
								throw new BuildException("ndk-build failed [{0}]", NDKBuildInfo.Arguments);
							}
						}
	
						// after ndk-build is called, we can now copy in the stl .so (ndk-build deletes old files)
						// copy libgnustl_shared.so to library (use 4.8 if possible, otherwise 4.6)
						if (bMakeSeparateApks)
						{
							CopySTL(UE4BuildPath, Arch);
							CopyGfxDebugger(UE4BuildPath, Arch);
						}
						else
						{
							foreach (string InnerArch in Arches)
							{
								CopySTL(UE4BuildPath, InnerArch);
								CopyGfxDebugger(UE4BuildPath, InnerArch);
							}
						}


						// remove any read only flags
						FileInfo DestFileInfo = new FileInfo(FinalSOName);
						DestFileInfo.Attributes = DestFileInfo.Attributes & ~FileAttributes.ReadOnly;

						// Use ant debug to build the .apk file
						Log.TraceInformation("{0} ====PERFORMING FINAL APK PACKAGE OPERATION=(with no library dependency compiling)=========", DateTime.Now.ToString());
						RunCommandLineProgramAndThrowOnError(UE4BuildPath, "cmd.exe", "/c \"" + GetAntPath() + "\" nodeps " + (bForDistribution ? "release" : "debug"));

						// make sure destination exists
						Directory.CreateDirectory(Path.GetDirectoryName(DestApkName));

						// do we need to sign for distro?
						if (bForDistribution)
						{
							// use diffeent source and dest apk's for signed mode
							string SourceApkName = UE4BuildPath + "/bin/" + ProjectName + "-release-unsigned.apk";
							SignApk(UE4BuildPath + "/SigningConfig.xml", SourceApkName, DestApkName);
						}
						else
						{
							// now copy to the final location
							File.Copy(UE4BuildPath + "/bin/" + ProjectName + "-debug" + ".apk", DestApkName, true);
						}
					}
				}
			}
		}

		private void SignApk(string ConfigFilePath, string SourceApk, string DestApk)
		{
			string JarCommandPath = Environment.ExpandEnvironmentVariables("%JAVA_HOME%/bin/jarsigner.exe");
			string ZipalignCommandPath = Environment.ExpandEnvironmentVariables("%ANDROID_HOME%/tools/zipalign.exe");

			if (!File.Exists(ConfigFilePath))
			{
				throw new BuildException("Unable to sign for Shipping without signing config file: '{0}", ConfigFilePath);
			}

			// open an Xml parser for the config file
			string ConfigFile = File.ReadAllText(ConfigFilePath);
			XmlReader Xml = XmlReader.Create(new StringReader(ConfigFile));

			string Alias = "UESigningKey";
			string KeystorePassword = "";
			string KeyPassword = "_sameaskeystore_";
			string Keystore = "UE.keystore";

			Xml.ReadToFollowing("ue-signing-config");
			bool bFinishedSection = false;
			while (Xml.Read() && !bFinishedSection)
			{
				switch (Xml.NodeType)
				{
					case XmlNodeType.Element:
						if (Xml.Name == "keyalias")
						{
							Alias = Xml.ReadElementContentAsString();
						}
						else if (Xml.Name == "keystorepassword")
						{
							KeystorePassword = Xml.ReadElementContentAsString();
						}
						else if (Xml.Name == "keypassword")
						{
							KeyPassword = Xml.ReadElementContentAsString();
						}
						else if (Xml.Name == "keystore")
						{
							Keystore = Xml.ReadElementContentAsString();
						}
						break;
					case XmlNodeType.EndElement:
						if (Xml.Name == "ue-signing-config")
						{
							bFinishedSection = true;
						}
						break;
				}
			}

			string CommandLine = "-sigalg SHA1withRSA -digestalg SHA1";
			CommandLine += " -storepass " + KeystorePassword;

			if (KeyPassword != "_sameaskeystore_")
			{
				CommandLine += " -keypass " + KeyPassword;
			}

			// put on the keystore
			CommandLine += " -keystore \"" + Keystore + "\"";

			// finish off the commandline
			CommandLine += " \"" + SourceApk + "\" " + Alias;

			// sign in-place
			ProcessStartInfo CallJarStartInfo = new ProcessStartInfo();
			CallJarStartInfo.WorkingDirectory = Path.GetDirectoryName(ConfigFilePath);
			CallJarStartInfo.FileName = JarCommandPath;
			CallJarStartInfo.Arguments = CommandLine;// string.Format("galg SHA1withRSA -digestalg SHA1 -keystore {1} {2} {3}",	 Password, Keystore, SourceApk, Alias);
			CallJarStartInfo.UseShellExecute = false;
			Console.WriteLine("\nRunning: {0} {1}", CallJarStartInfo.FileName, CallJarStartInfo.Arguments);
			Process CallAnt = new Process();
			CallAnt.StartInfo = CallJarStartInfo;
			CallAnt.Start();
			CallAnt.WaitForExit();

			if (File.Exists(DestApk))
			{
				File.Delete(DestApk);
			}

			// now we need to zipalign the apk to the final destination (to 4 bytes, must be 4)
			ProcessStartInfo CallZipStartInfo = new ProcessStartInfo();
			CallZipStartInfo.WorkingDirectory = Path.GetDirectoryName(ConfigFilePath);
			CallZipStartInfo.FileName = ZipalignCommandPath;
			CallZipStartInfo.Arguments = string.Format("4 \"{0}\" \"{1}\"", SourceApk, DestApk);
			CallZipStartInfo.UseShellExecute = false;
			Console.WriteLine("\nRunning: {0} {1}", CallZipStartInfo.FileName, CallZipStartInfo.Arguments);
			Process CallZip = new Process();
			CallZip.StartInfo = CallZipStartInfo;
			CallZip.Start();
			CallZip.WaitForExit();
		}

		public override bool PrepTargetForDeployment(UEBuildTarget InTarget)
		{
			// we need to strip architecture from any of the output paths
			string BaseSoName = AndroidToolChain.RemoveArchName(InTarget.OutputPaths[0]);
			// this always makes a merged .apk since for debugging, there's no way to know which one to run
			MakeApk(InTarget.AppName, InTarget.ProjectDirectory, BaseSoName, BuildConfiguration.RelativeEnginePath, bForDistribution:false, CookFlavor:"", bMakeSeparateApks:false, bIncrementalPackage:true);
			return true;
		}

		public static bool ShouldMakeSeparateApks()
		{
			// check to see if the project wants separate apks
			ConfigCacheIni Ini = new ConfigCacheIni(UnrealTargetPlatform.Android, "Engine", UnrealBuildTool.GetUProjectPath());
			bool bSeparateApks = false;
			Ini.GetBool("/Script/AndroidRuntimeSettings.AndroidRuntimeSettings", "bSplitIntoSeparateApks", out bSeparateApks);

			return bSeparateApks;
		}

		public override bool PrepForUATPackageOrDeploy(string ProjectName, string ProjectDirectory, string ExecutablePath, string EngineDirectory, bool bForDistribution, string CookFlavor)
		{
			MakeApk(ProjectName, ProjectDirectory, ExecutablePath, EngineDirectory, bForDistribution, CookFlavor, ShouldMakeSeparateApks(), bIncrementalPackage:false);
			return true;
		}

		public static void OutputReceivedDataEventHandler(Object Sender, DataReceivedEventArgs Line)
		{
			if ((Line != null) && (Line.Data != null))
			{
				Log.TraceInformation(Line.Data);
			}
		}
	}
}
