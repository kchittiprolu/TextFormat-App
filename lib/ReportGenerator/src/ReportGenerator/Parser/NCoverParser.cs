﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using log4net;

namespace Palmmedia.ReportGenerator.Parser
{
    /// <summary>
    /// Parser for XML reports generated by NCover.
    /// </summary>
    public class NCoverParser : ParserBase, IParser
    {
        /// <summary>
        /// The logger.
        /// </summary>
        private static readonly ILog logger = LogManager.GetLogger(typeof(NCoverParser));

        /// <summary>
        /// The covered assemblies. The HashSet is used for caching.
        /// </summary>
        private HashSet<string> assembliesHashSet;

        /// <summary>
        /// Initializes a new instance of the <see cref="NCoverParser"/> class.
        /// </summary>
        /// <param name="report">The report file as XContainer.</param>
        public NCoverParser(XContainer report)
            : base(report)
        {
            // Init the HashSet containing the covered assemblies
            this.assembliesHashSet = new HashSet<string>(report.Descendants("module").Select(module => module.Attribute("assembly").Value));

            // Init the Dictionary containing the line coverage information
            Parallel.ForEach(
                this.Assemblies(),
                (assembly) =>
                {
                    logger.DebugFormat("  Current Assembly: {0}", assembly);

                    Parallel.ForEach(
                        this.ClassesInAssembly(assembly),
                        (clazz) =>
                        {
                            Parallel.ForEach(
                                this.FilesOfClass(assembly, clazz),
                                (file) =>
                                {
                                    var seqpntsOfFile = report.Descendants("module")
                                        .Where(type => type.Attribute("assembly").Value.Equals(assembly))
                                        .Elements("method").Where(method => method.Attribute("class").Value.StartsWith(clazz, StringComparison.Ordinal))
                                        .Descendants("seqpnt")
                                        .Where(seqpnt => seqpnt.Attribute("document").Value.Equals(file) && seqpnt.Attribute("line").Value != "16707566")
                                        .OrderBy(seqpnt => int.Parse(seqpnt.Attribute("line").Value, CultureInfo.InvariantCulture))
                                        .ToArray();

                                    if (seqpntsOfFile.Length > 0)
                                    {
                                        var coverage = new int[int.Parse(seqpntsOfFile.Last().Attribute("line").Value, CultureInfo.InvariantCulture) + 1];

                                        for (int i = 0; i < coverage.Length; i++)
                                        {
                                            coverage[i] = -1;
                                        }

                                        foreach (var seqpnt in seqpntsOfFile)
                                        {
                                            var index = int.Parse(seqpnt.Attribute("line").Value, CultureInfo.InvariantCulture);
                                            var value = int.Parse(seqpnt.Attribute("visitcount").Value, CultureInfo.InvariantCulture);
                                            coverage[index] = coverage[index] == -1 ? value : coverage[index] + value;
                                        }

                                        this.LineCoverageByFileDictionary.TryAdd(assembly + "_" + clazz + "_" + file, coverage);
                                    }
                                });
                        });
                });
        }

        /// <summary>
        /// Determine all covered files.
        /// </summary>
        /// <returns>All covered files.</returns>
        public IEnumerable<string> Files()
        {
            return this.Report.Descendants("seqpnt")
                .Where(seqpnt => seqpnt.Attribute("document") != null)
                .Select(seqpnt => seqpnt.Attribute("document").Value)
                .Distinct()
                .ToArray();
        }

        /// <summary>
        /// Determine all covered assemblies.
        /// </summary>
        /// <returns>All covered assemblies.</returns>
        public IEnumerable<string> Assemblies()
        {
            return this.assembliesHashSet
                .OrderBy(value => value)
                .ToArray();
        }

        /// <summary>
        /// Determine all covered classes within an assembly.
        /// </summary>
        /// <param name="assemblyName">The name of the assembly.</param>
        /// <returns>All covered classes within an assembly.</returns>
        public IEnumerable<string> ClassesInAssembly(string assemblyName)
        {
            if (!this.assembliesHashSet.Contains(assemblyName))
            {
                return new string[] { };
            }

            return this.Report.Descendants("module")
                .Where(module => module.Attribute("assembly").Value.Equals(assemblyName))
                .Elements("method")
                .Select(method => method.Attribute("class").Value)
                .Where(value => !value.Contains("__"))
                .Distinct()
                .OrderBy(name => name)
                .ToArray();
        }

        /// <summary>
        /// Determine all files a class is defined in.
        /// </summary>
        /// <param name="assemblyName">The name of the assembly.</param>
        /// <param name="className">The name of the class.</param>
        /// <returns>All files a class is defined in.</returns>
        public IEnumerable<string> FilesOfClass(string assemblyName, string className)
        {
            if (!this.assembliesHashSet.Contains(assemblyName))
            {
                return new string[] { };
            }

            return this.Report.Descendants("module")
                .Where(module => module.Attribute("assembly").Value.Equals(assemblyName)).Elements("method")
                .Where(method => method.Attribute("class").Value.Equals(className))
                .Elements("seqpnt").Select(seqpnt => seqpnt.Attribute("document").Value)
                .Distinct()
                .ToArray();
        }

        /// <summary>
        /// Determine how often a line of code has been covered.
        /// If line could not be covered at all -1 is returned.
        /// </summary>
        /// <param name="assemblyName">The name of the assembly.</param>
        /// <param name="className">The name of the class.</param>
        /// <param name="fileName">The name of the file.</param>
        /// <param name="lineNumber">The number of the line (starting with 1, not zero based).</param>
        /// <returns>Number of visits.</returns>
        public int NumberOfLineVisits(string assemblyName, string className, string fileName, int lineNumber)
        {
            if (!this.assembliesHashSet.Contains(assemblyName))
            {
                return -1;
            }

            var key = assemblyName + "_" + className + "_" + fileName;
            if (this.LineCoverageByFileDictionary.ContainsKey(key))
            {
                var coverage = this.LineCoverageByFileDictionary[key];
                return (coverage.Length > lineNumber) ? coverage[lineNumber] : -1;
            }
            else
            {
                return -1;
            }
        }
    }
}
