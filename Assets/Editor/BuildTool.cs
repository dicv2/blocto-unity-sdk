using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
// ReSharper disable ComplexConditionExpression

namespace Editor
{
    public class BuildTool 
    {
        [MenuItem("BuildTool/Build")]
        private static void Build()
        {
            Console.WriteLine($"Start build...........{DateTime.Now}");
            CustomizedCommandLine();
            if(_action.ToLower() == "exportpackage")
            {
                Console.WriteLine($"Package name: {_exportName}, Build version: {_versionNumber}");
                PlayerSettings.stripEngineCode = false;
                ExportPackage(_exportName, _versionNumber);
                Console.WriteLine("Complete export package");
            }
            
            Console.WriteLine("Complete build process");
        }

 
        private static string _destinationPath;
        private static string _exportName;
        private static string _versionNumber;
        private static string _action;
        private static void CustomizedCommandLine()
        {
            Dictionary<string, Action<string>> cmdActions = new Dictionary<string, Action<string>>
                                                            {
                                                                {
                                                                    "-destinationPath", delegate(string argument)
                                                                                        {
                                                                                            _destinationPath = argument;
                                                                                        }
                                                                },
                                                                {
                                                                    "-exportName", delegate(string argument)
                                                                                    {
                                                                                        _exportName = argument;
                                                                                    }
                                                                },
                                                                {
                                                                    "-buildVersion", delegate(string argument)
                                                                                     {
                                                                                         _versionNumber = argument;
                                                                                     }
                                                                },
                                                                {
                                                                    "-action", delegate(string argument)
                                                                               {
                                                                                   _action = argument;
                                                                               }
                                                                },
                                                            };
 
            Action<string> actionCache;
            var cmdArguments = Environment.GetCommandLineArgs();
 
            for (var count = 0; count < cmdArguments.Length; count++)
            {
                Console.WriteLine($"Argument: {cmdArguments[count]}");
                if (!cmdActions.ContainsKey(cmdArguments[count])) continue;
                actionCache = cmdActions[cmdArguments[count]];
                actionCache(cmdArguments[count + 1]);
            }
 
            if (string.IsNullOrEmpty(_destinationPath))
            {
                _destinationPath = Path.GetDirectoryName(Application.dataPath);
            }
        }
 
        private static void ExportPackage(string exportName, string buildVersion)
        {
            Console.WriteLine($"In export package function, Package Name: {exportName}, Build version: {buildVersion}");
            var directories = new List<string>();
            var result = Enum.TryParse(typeof(ExportTypeEnum), exportName, true, out var packageType);
            if(result == false)
            {
                throw new ArgumentException("Package type not found.");
            }
        
            Console.WriteLine($"Package type: {((ExportTypeEnum)packageType).ToString()}");
            switch ((ExportTypeEnum)packageType)
            {
                case ExportTypeEnum.FCL:
                    var fclAssetsPaths = new List<string>
                                         {
                                             "Assets/Plugins/Flow/FCL",
                                             "Assets/Plugins/Flow/Flow.Net.Sdk",
                                             "Assets/Plugins/Flow/Flow.Net.Sdk.Client.Unity",
                                             "Assets/Plugins/Flow/Flow.Net.Sdk.Core",
                                             "Assets/Plugins/Flow/Flow.Net.Sdk.Utility",
                                             "Assets/Plugins/Dll",
                                             "Assets/Plugins/System.ComponentModel.Annotations.dll"
                                         };
                    AssetDatabase.ExportPackage(fclAssetsPaths.ToArray(), $"release/fcl-unity/fcl-unity.{buildVersion}.unitypackage", ExportPackageOptions.Recurse | ExportPackageOptions.Default);
                    Debug.Log("FCL export success.");
                    break;
                case ExportTypeEnum.BloctoUnitySDK:
                    directories.Add("Assets/Plugins/Blocto.Sdk/Core");
                    directories.Add("Assets/Plugins/Blocto.Sdk/Flow");
                    directories.Add("Assets/Plugins/Dll");
                    directories.Add($"Assets/Plugins/Android");
                    directories.Add($"Assets/Plugins/iOS/UnityIosPlugin");
                    AssetDatabase.ExportPackage(directories.ToArray(), $"release/blocto-unity-sdk/Blocto-unity-sdk.{buildVersion}.unitypackage", ExportPackageOptions.Recurse | ExportPackageOptions.Default);
                    Debug.Log("Blocto-unity-SDK export successed.");
                    break;
                case ExportTypeEnum.Core:
                    var coreOutputPath = $"release/{buildVersion}";
                    Console.WriteLine($"In core package process, Output path: {coreOutputPath}, Directory exist: {Directory.Exists(coreOutputPath)}");

                    if(Directory.Exists(coreOutputPath) == false)
                    {
                        Console.WriteLine("Create directory.");
                        Directory.CreateDirectory(coreOutputPath);
                    }
                
                    var coreDirInfo = new DirectoryInfo($"{Application.dataPath}/Plugins/Blocto.Sdk/Core");
                    var coreDirPaths = coreDirInfo.GetDirectories().Select(p => {
                                                                               var tmp = p.FullName.Split("Assets/")[1];
                                                                               return $"Assets/{tmp}";
                                                                           }).ToList(); 
                    directories.AddRange(coreDirPaths);
                    directories.Add("Assets/Plugins/Dll");
                    directories.Add($"Assets/Plugins/Android");
                    directories.Add($"Assets/Plugins/iOS/UnityIosPlugin");
                    AssetDatabase.ExportPackage(directories.ToArray(), $"release/{buildVersion}/Portto.Blocto.Core.{buildVersion}.unitypackage", ExportPackageOptions.Recurse | ExportPackageOptions.Default);
                    Debug.Log("Portto.Blocto.Core export success.");
                    Console.WriteLine("Portto.Blocto.Core export success.");
                    break;
                case ExportTypeEnum.Solana:
                    var solanaOutputPath = $"release/{buildVersion}";
                    if(Directory.Exists(solanaOutputPath) == false)
                    {
                        Directory.CreateDirectory(solanaOutputPath);
                    }

                    var solanaDirInfo = new DirectoryInfo($"{Application.dataPath}/Plugins/Blocto.Sdk/Solana");
                    var solanaDirPaths = solanaDirInfo.GetDirectories().Select(p => {
                                                                                   var tmp = p.FullName.Split("Assets/")[1];
                                                                                   return $"Assets/{tmp}";
                                                                               }).ToList(); 
                    directories.AddRange(solanaDirPaths);
                    directories.AddRange(new List<string>
                                         {
                                             $"Assets/Plugins/Blocto.Sdk/Solana/BloctoWalletProvider.cs",
                                         });
                    AssetDatabase.ExportPackage(directories.ToArray(), $"release/{buildVersion}/Portto.Blocto.Solana.{buildVersion}.unitypackage", ExportPackageOptions.Recurse | ExportPackageOptions.Default);
                    Debug.Log("Protto.Blocto.Solana export successes.");
                    break;
                case ExportTypeEnum.Evm:
                    var evmOutputPath = $"release/{buildVersion}";
                    if(Directory.Exists(evmOutputPath) == false)
                    {
                        Directory.CreateDirectory(evmOutputPath);
                    }

                    var evmDirInfo = new DirectoryInfo($"{Application.dataPath}/Plugins/Blocto.Sdk/Evm");
                    var evmDirPaths = evmDirInfo.GetDirectories().Select(p => {
                                                                             var tmp = p.FullName.Split("Assets/")[1];
                                                                             return $"Assets/{tmp}";
                                                                         }).ToList(); 
                    directories.AddRange(evmDirPaths);
                    directories.AddRange(new List<string>
                                         {
                                             "Assets/Plugins/Blocto.Sdk/Evm/BloctoWalletProvider.cs",
                                             "Assets/Plugins/Blocto.Sdk/Evm/EthereumClient.cs"
                                         });
                    AssetDatabase.ExportPackage(directories.ToArray(), $"release/{buildVersion}/Portto.Blocto.Evm.{buildVersion}.unitypackage", ExportPackageOptions.Recurse | ExportPackageOptions.Default);
                    Debug.Log("Protto.Blocto.Evm export successed."); 
                    break;
                case ExportTypeEnum.Aptos:
                    var aptosOutputPath = $"release/{buildVersion}";
                    if(Directory.Exists(aptosOutputPath) == false)
                    {
                        Directory.CreateDirectory(aptosOutputPath);
                    }

                    var aptosDirInfo = new DirectoryInfo($"{Application.dataPath}/Plugins/Blocto.Sdk/Aptos");
                    var aptosDirPaths = aptosDirInfo.GetDirectories().Select(p => {
                                                                             var tmp = p.FullName.Split("Assets/")[1];
                                                                             return $"Assets/{tmp}";
                                                                         }).ToList(); 
                    directories.AddRange(aptosDirPaths);
                    directories.AddRange(new List<string>
                                         {
                                             "Assets/Plugins/Blocto.Sdk/Aptos/BloctoWalletProvider.cs"
                                         });
                    AssetDatabase.ExportPackage(directories.ToArray(), $"release/{buildVersion}/Portto.Blocto.Aptos.{buildVersion}.unitypackage", ExportPackageOptions.Recurse | ExportPackageOptions.Default);
                    Debug.Log("Protto.Blocto.Aptos export successed."); 
                    break;
                case ExportTypeEnum.Flow:
                    var flowOutputPath = $"release/{buildVersion}";
                    if(Directory.Exists(flowOutputPath) == false)
                    {
                        Directory.CreateDirectory(flowOutputPath);
                    }

                    var flowDirInfo = new DirectoryInfo($"{Application.dataPath}/Plugins/Blocto.Sdk/Flow");
                    var flowDirPaths = flowDirInfo.GetDirectories().Select(p => {
                                                                             var tmp = p.FullName.Split("Assets/")[1];
                                                                             return $"Assets/{tmp}";
                                                                         }).ToList(); 
                    directories.AddRange(flowDirPaths);
                    directories.AddRange(new List<string>
                                         {
                                             "Assets/Plugins/Blocto.Sdk/Flow/BloctoWalletProvider.cs",
                                             "Assets/Plugins/Blocto.Sdk/Flow/IBloctoWalletProvider.cs"
                                         });
                    AssetDatabase.ExportPackage(directories.ToArray(), $"release/{buildVersion}/Portto.Blocto.Flow.{buildVersion}.unitypackage", ExportPackageOptions.Recurse | ExportPackageOptions.Default);
                    Debug.Log("Protto.Blocto.Flow export successes."); 
                    break;
                default:
                    Console.WriteLine("Default");
                    throw new ArgumentOutOfRangeException();
            }
            
            Console.WriteLine($"Export package process complete, {DateTime.Now}");
        }
    }
}