﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Micronetes.Hosting.Model
{
    public class Application
    {
        public string ContextDirectory { get; set; } = Directory.GetCurrentDirectory();

        public Application(ServiceDescription[] services)
        {
            var map = new Dictionary<string, Service>();

            // TODO: Do validation here
            foreach (var s in services)
            {
                s.Replicas ??= 1;
                map[s.Name] = new Service { Description = s };
            }

            Services = map;
        }

        public static Application FromYaml(string path)
        {
            var fullPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var descriptions = deserializer.Deserialize<ServiceDescription[]>(new StringReader(File.ReadAllText(path)));

            var contextDirectory = Path.GetDirectoryName(fullPath);

            foreach (var d in descriptions)
            {
                if (d.ProjectFile == null)
                {
                    continue;
                }

                // Try to populate more from launch settings
                var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(contextDirectory, d.ProjectFile)));
                var launchSettingsPath = Path.Combine(projectDirectory, "Properties", "launchSettings.json");

                if (File.Exists(launchSettingsPath))
                {
                    // If there's a launchSettings.json, then use it to get addresses
                    var root = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(launchSettingsPath));
                    var key = Path.GetFileNameWithoutExtension(d.ProjectFile);
                    var profiles = root.GetProperty("profiles");
                    if (profiles.TryGetProperty(key, out var projectSettings))
                    {
                        // Only do this if there are no bindings
                        //if (d.Bindings.Count == 0)
                        //{
                        //    var addresses = projectSettings.GetProperty("applicationUrl").GetString()?.Split(';');

                        //    foreach (var address in addresses)
                        //    {
                        //        d.Bindings.Add(new ServiceBinding
                        //        {
                        //            Name = "default",
                        //            ConnectionString = address,
                        //            Protocol = "http"
                        //        });
                        //    }
                        //}
                    }
                }
            }

            return new Application(descriptions)
            {
                // Use the file location as the context when loading from a file
                ContextDirectory = contextDirectory
            };
        }

        public static Application FromProject(string path)
        {
            var fullPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));

            return new Application(new ServiceDescription[0])
            {
                ContextDirectory = Path.GetDirectoryName(fullPath)
            };
        }

        public static Application FromSolution(string path)
        {
            var fullPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));

            return new Application(new ServiceDescription[0])
            {
                ContextDirectory = Path.GetDirectoryName(fullPath)
            };
        }

        public Dictionary<string, Service> Services { get; }

        internal void PopulateEnvironment(Service service, Action<string, string> set)
        {
            if (service.Description.Configuration != null)
            {
                // Inject normal configuration
                foreach (var pair in service.Description.Configuration)
                {
                    set(pair.Key, pair.Value);
                }
            }

            void SetBinding(string bindingName, ServiceBinding b)
            {
                if (!string.IsNullOrEmpty(b.ConnectionString))
                {
                    // Special case for connection strings
                    set($"CONNECTIONSTRING__{bindingName}", b.ConnectionString);
                }

                if (!string.IsNullOrEmpty(b.Protocol))
                {
                    // ASPNET.Core specific
                    set($"{bindingName}__SERVICE__PROTOCOL", b.Protocol);
                    set($"{bindingName}_SERVICE_PROTOCOL", b.Protocol);
                }

                if (b.Port != null)
                {
                    set($"{bindingName}__SERVICE__PORT", b.Port.ToString());
                    set($"{bindingName}_SERVICE_PORT", b.Port.ToString());
                }

                set($"{bindingName}__SERVICE__HOST", b.Host ?? "localhost");
                set($"{bindingName}_SERVICE_HOST", b.Host ?? "localhost");
            }

            // Inject dependency information
            foreach (var s in Services.Values)
            {
                foreach (var b in s.Description.Bindings)
                {
                    if (string.IsNullOrEmpty(b.Name))
                    {
                        SetBinding(s.Description.Name.ToUpper(), b);
                    }
                    else
                    {
                        string bindingName = $"{s.Description.Name.ToUpper()}__{b.Name.ToUpper()}";
                        SetBinding(bindingName, b);
                    }
                }

                if (s.Description.Bindings.Count == 1)
                {
                    SetBinding(s.Description.Name.ToUpper(), s.Description.Bindings[0]);
                }
            }
        }
    }
}
