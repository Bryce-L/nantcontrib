//
// NAntContrib
// Copyright (C) 2001-2002 Gerry Shaw
//
//
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307 USA
//

// Aaron A. Anderson (aaron@skypoint.com | aaron.anderson@farmcreditbank.com)
// Kevin Dente (kevin_d@mindspring.com)

using System;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

using SourceForge.NAnt;
using SourceForge.NAnt.Tasks;
using SourceForge.NAnt.Attributes;

namespace NAnt.Contrib.Tasks {


    /// <summary>Compiles Microsoft Visual Basic 6 programs.</summary>
    /// <remarks>
    ///     <para>Uses the VB6.EXE executable included with the Visual Basic 6 environment.</para>
    ///     <para>The compiler uses the settings and source files specified in the project or group file.</para>
    /// </remarks>
    /// <example>
    ///     <para>Build the project <c>HelloWorld.vbp</c> in the <c>build</c> directory.</para>
    ///     <code>
    ///      <![CDATA[
    ///         <vb6 project="HelloWorld.vbp" outdir="build" />
    ///      ]]>
    ///     </code>
    /// </example>

    [TaskName("vb6")]
    public class Vb6Task : ExternalProgramBase {

        string _projectFile = null;
        string _outdir = null;
        string _programArguments = null;
        string _errorFile = null;
        bool _checkReferences = true;
        
        /// <summary>Output directory for the compilation target.  If the directory does not exist, the task will create it.</summary>
        [TaskAttribute("outdir")]
        public string  OutDir       { get { return _outdir; } set {_outdir = value;}} 

        /// <summary>Visual Basic project or group file.</summary>
        [TaskAttribute("project", Required=true)]
        public string  ProjectFile         { get { return _projectFile; } set {_projectFile = value;}} 

        /// <summary>
        /// Determines whether project references are checked when deciding whether
        /// the project needs to be recompiled (<c>true</c>/<c>false</c>).
        /// </summary>
        [BooleanValidator()]
        [TaskAttribute("checkreferences")]
        public bool CheckReferences { get { return Convert.ToBoolean(_checkReferences); } set { _checkReferences = value; }}

        /// <summary>File</summary>
        [TaskAttribute("errorfile", Required=true)]
        public string  ErrorFile         { get { return _errorFile; } set {_errorFile = value;}} 

        public override string ProgramFileName  { get { return Name; } }       
        public override string ProgramArguments { get { return _programArguments; } }

        protected virtual bool NeedsCompiling() {
            // return true as soon as we know we need to compile

            if (String.Compare(Path.GetExtension(ProjectFile), ".VBG", true) == 0) {
                // The project file is a Visual Basic group file (VBG). 
                // We need to check each subproject in the group
                StringCollection projectFiles = ParseGroupFile(ProjectFile);
                foreach (string projectFile in projectFiles) {
                    if (ProjectNeedsCompiling(projectFile))
                        return true;
                }
            }
            else {
                // The project file is a Visual Basic project file (VBP)
                return ProjectNeedsCompiling(ProjectFile);
            }

            return false;
        }

        /// <summary>Parses a VB group file and extract the file names of the sub-projects in the group.</summary>
        /// 
        /// <param name="groupFile">The file name of the group file.</param>
        /// <returns>A string collection containing the list of sub-projects in the group</returns>
        protected StringCollection ParseGroupFile(string groupFile) {
            StringCollection projectFiles = new StringCollection();

            if (!File.Exists(groupFile)) {
                throw new BuildException("Visual Basic group file " + groupFile + " does not exist.");
            }

            string fileLine = null;
        
            // Regexp that extracts INI-style "key=value" entries used in the VBP
            Regex keyValueRegEx = new Regex(@"(?<key>\w+)\s*=\s*(?<value>.*)\s*$");

            string key = String.Empty;
            string keyValue = String.Empty;
            
            Match match = null;
            using (StreamReader reader = new StreamReader(Project.GetFullPath(groupFile), Encoding.ASCII)) {
                while( (fileLine = reader.ReadLine()) != null) {
                    match = keyValueRegEx.Match(fileLine);
                    if (match.Success) {
                        key = match.Groups["key"].Value;
                        keyValue = match.Groups["value"].Value;

                        if ((key == "StartupProject") || (key == "Project")) {
                            // This is a project file - get the file name and add it to the project list
                            projectFiles.Add(keyValue);							
                        }
                    }
                }
                reader.Close();
            }
            
            return projectFiles;
        }

        /// <summary>
        /// Determines if a VB project needs to be recompiled by comparing the timestamp of 
        /// the project's files and references to the timestamp of the last built version.
        /// </summary>
        /// <param name="projectFile">The file name of the project file.</param>
        /// <returns>true if the project should be compiled, false otherwise</returns>
        protected bool ProjectNeedsCompiling(string projectFile) {
            // return true as soon as we know we need to compile
        
            FileSet sources = new FileSet();
            sources.BaseDirectory = BaseDirectory;
            
            FileSet references = new FileSet();
            references.BaseDirectory = BaseDirectory;

            string outputFile = ParseProjectFile(projectFile, sources, references);

            FileInfo outputFileInfo = new FileInfo(OutDir != null ? Path.Combine(OutDir, outputFile) : outputFile) ;
            if (!outputFileInfo.Exists) {
                Log.WriteLineIf(Verbose, LogPrefix + "Output file {0} does not exist, recompiling.", outputFileInfo.FullName);
                return true;
            }

            //HACK:(POSSIBLY)Is there any other way to pass in a single file to check to see if it needs to be updated?
            StringCollection fileset = new StringCollection();
            fileset.Add(outputFileInfo.FullName);
            string fileName;
            fileName = FileSet.FindMoreRecentLastWriteTime(fileset, outputFileInfo.LastWriteTime);
            if (fileName != null) {
                Log.WriteLineIf(Verbose, LogPrefix + "{0} is out of date, recompiling.", fileName);
                return true;
            }

            fileName = FileSet.FindMoreRecentLastWriteTime(sources.FileNames, outputFileInfo.LastWriteTime);
            if (fileName != null) {
                Log.WriteLineIf(Verbose, LogPrefix + "{0} is out of date, recompiling.", fileName);
                return true;
            }

            if (_checkReferences) {
                fileName = FileSet.FindMoreRecentLastWriteTime(references.FileNames, outputFileInfo.LastWriteTime);
                if (fileName != null) {
                    Log.WriteLineIf(Verbose, LogPrefix + "{0} is out of date, recompiling.", fileName);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Parses a VB project file and extracts the source files, reference files, and 
        /// the name of the compiled file for the project.
        /// </summary>
        /// <param name="projectFile">The filename of the project file.</param>
        /// <param name="sources">
        /// A fileset representing the source files of the project, which will
        /// populated by the method.
        ///	</param>
        /// <param name="references">
        /// A fileset representing the references of the project, which will
        /// populated by the method.
        /// </param>
        /// <returns>A string containing the output file name for the project.</returns>
        private string ParseProjectFile(string projectFile, FileSet sources, FileSet references) {
            
            if (!File.Exists(projectFile)) {
                throw new BuildException("Visual Basic project file " + projectFile + " does not exist.");
            }

            string outputFile = null;
            string fileLine = null;
            string projectName = null;
            string projectType = null;
        
            // Regexp that extracts INI-style "key=value" entries used in the VBP
            Regex keyValueRegEx = new Regex(@"(?<key>\w+)\s*=\s*(?<value>.*)\s*$");

            // Regexp that extracts source file entries from the VBP (Class=,Module=,Form=,UserControl=)
            Regex codeRegEx = new Regex(@"(Class|Module)\s*=\s*\w*;\s*(?<filename>.*)\s*$");

            // Regexp that extracts reference entries from the VBP (Reference=)
            Regex referenceRegEx = new Regex(@"Reference\s*=\s*\*\\G{[0-9\-A-Fa-f]*}\#[0-9\.]*\#[0-9]\#(?<tlbname>.*)\#");

            string key = String.Empty;
            string keyValue = String.Empty;
            
            Match match = null;
            using (StreamReader reader = new StreamReader(Project.GetFullPath(projectFile), Encoding.ASCII)) {
                while( (fileLine = reader.ReadLine()) != null) {
                    match = keyValueRegEx.Match(fileLine);
                    if (match.Success) {
                        key = match.Groups["key"].Value;
                        keyValue = match.Groups["value"].Value;

                        if ((key == "Class") || (key == "Module")) {
                            // This is a class or module source file - extract the file name and add it to the sources fileset
                            // The entry is of the form "Class=ClassName;ClassFile.cls"
                            match = codeRegEx.Match(fileLine);
                            if (match.Success) {
                                sources.Includes.Add(match.Groups["filename"].Value);
                            }
                        }
                        else if ((key == "Form") || (key == "UserControl") || (key == "PropertyPage")) {
                            // This is a form, control, or property page source file - add the file name to the sources fileset
                            // The entry is of the form "Form=Form1.frm"
                            sources.Includes.Add(keyValue);
                        }
                        else if (key == "Reference") {
                            // This is a source file - extract the reference name and add it to the references fileset
                            match = referenceRegEx.Match(fileLine);
                            if (match.Success) {
                                references.Includes.Add(match.Groups["tlbname"].Value);
                            }
                        }
                        else if (key == "ExeName32") {
                            // Store away the built file name so that we can check against it later
                            // If the project was never built in the IDE, or the project file wasn't saved
                            // after the build occurred, this setting won't exist. In that case, VB uses the
                            // ProjectName as the DLL/EXE name
                            outputFile = keyValue.Trim('"');
                        }
                        else if (key == "Type") {
                            // Store away the project type - we may need it to construct the built
                            // file name if ExeName32 doesn't exist
                            projectType = keyValue;
                        }
                        else if (key == "Name") {
                            // Store away the project name - we may need it to construct the built
                            // file name if ExeName32 doesn't exist
                            projectName = keyValue.Trim('"');
                        }
                    }
                }
                reader.Close();
            }

            if (outputFile == null) {
                // The output file name wasn't specified in the project file, so
                // We need to figure out the output file name from the project name and type
                if ((projectType == "Exe") || (projectType == "OleExe")) {
                    outputFile = Path.ChangeExtension(projectName, ".exe");
                }
                else if (projectType == "OleDll") {
                    outputFile = Path.ChangeExtension(projectName, ".dll");
                }
                else if (projectType == "Control") {
                    outputFile = Path.ChangeExtension(projectName, ".ocx");
                }
            }

            return outputFile;
        }

        protected override void ExecuteTask() { 

            Log.WriteLineIf(Verbose, LogPrefix + "Building project {0}", ProjectFile);
            if (NeedsCompiling()) {

                //Using a stringbuilder vs. StreamWriter since this program will not accept response files.
                StringBuilder writer = new StringBuilder();

                try {
                    writer.AppendFormat(" /make \"{0}\"", ProjectFile);

                    if (_outdir == null)
                        _outdir = BaseDirectory;
                    else {
                        if (!Directory.Exists(_outdir)) {
                            Directory.CreateDirectory(_outdir);
                        }
                    }

                    writer.AppendFormat(" /outdir \"{0}\"", _outdir);

                    if (_errorFile != null) {
                        writer.AppendFormat( " /out \"{0}\"", _errorFile);
                    }

                    // call base class to do the work
                    _programArguments = writer.ToString();
                    base.ExecuteTask();

                } finally {
                    writer = null;
                }
            }
        }
    }
}
