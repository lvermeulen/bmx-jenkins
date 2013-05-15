﻿using System;
using System.IO;
using Inedo.BuildMaster;
using Inedo.BuildMaster.Extensibility.Actions;
using Inedo.BuildMaster.Extensibility.Agents;
using Inedo.BuildMaster.Web;

namespace Inedo.BuildMasterExtensions.Jenkins
{
    /// <summary>
    /// Gets an artifact from a Jenkins server.
    /// </summary>
    [ActionProperties(
        "Get Jenkins Artifact",
        "Gets an artifact from a Jenkins server.",
        "Jenkins", 
        DefaultToLocalServer = true)]
    [RequiresInterface(typeof(IFileOperationsExecuter))]
    [RequiresInterface(typeof(IRemoteZip))]
    [CustomEditor(typeof(GetArtifactActionEditor))]
    public sealed class GetArtifactAction : JenkinsActionBase
    {
        /// <summary>
        /// Gets or sets the name of the artifact.
        /// </summary>
        [Persistent]
        public string ArtifactName { get; set; }

        /// <summary>
        /// Gets or sets the job id.
        /// </summary>
        [Persistent]
        public string Job { get; set; }

        /// <summary>
        /// Gets or sets the build number.
        /// </summary>
        [Persistent]
        public string BuildNumber { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether [extract files to target directory].
        /// </summary>
        [Persistent]
        public bool ExtractFilesToTargetDirectory { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="GetArtifactAction"/> class.
        /// </summary>
        public GetArtifactAction()
        {
            this.ExtractFilesToTargetDirectory = true;
        }

        /// <summary>
        /// Returns a <see cref="System.String"/> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String"/> that represents this instance.
        /// </returns>
        /// <remarks>
        /// This should return a user-friendly string describing what the Action does
        /// and the state of its important persistent properties.
        /// </remarks>
        public override string ToString()
        {
            return string.Format(
                "Get the artifact \"{0}\" of Build #{1} of the \"{2}\" job from Jenkins and {3} to {4}.", 
                this.ArtifactName, 
                this.BuildNumber, 
                this.Job,
                this.ExtractFilesToTargetDirectory ? "deploy its contents" : "copy the artifact",
                Util.CoalesceStr(this.OverriddenTargetDirectory, "the default directory")
            );
        }

        protected override void Execute()
        {
            LogDebug("Downloading Jenkins artifact \"{0}\" to {1}", this.ArtifactName, this.RemoteConfiguration.TargetDirectory);
            int build = GetBuildNumber(this.BuildNumber);
            var artifacts = ListArtifacts(build.ToString());
            if (this.ArtifactName.Trim().ToUpperInvariant() == "*")
            {
                foreach (var a in artifacts)
                {
                    ProcessArtifact(build, a.Key, a.Value);
                }
                return;
            }
            if (!artifacts.ContainsKey(this.ArtifactName))
            {
                LogError("The artifact {0} does not exist for build {1} of job {2}", this.ArtifactName, build, this.Job);
                return;
            }
            ProcessArtifact(build, this.ArtifactName,artifacts[this.ArtifactName]);
        }

        private bool ProcessArtifact(int BuildNumber, string BaseName, string RelativePath)
        {
            string tempFile = System.IO.Path.GetTempFileName();
            try
            {
                var result = GetArtifact(BuildNumber, RelativePath, tempFile);
                if (!result)
                {
                    LogError("There was an error retrieving the {0} artifact for build {1} of job {2}", this.ArtifactName, BuildNumber, this.Job);
                    return false;
                }
                using (var agent = (IFileOperationsExecuter)Util.Agents.CreateAgentFromId(this.ServerId))
                {
                    if (this.ExtractFilesToTargetDirectory)
                    {
                        LogDebug("Transferring artifact to {0} before extracting...", this.RemoteConfiguration.TempDirectory);
                        string remoteTempPath = agent.CombinePath(this.RemoteConfiguration.TempDirectory, BaseName);
                        agent.WriteFile(
                            remoteTempPath,
                            null,
                            null,
                            File.ReadAllBytes(tempFile),
                            false
                        );

                        LogDebug("Extracting Jenkins artifact to {0}...", this.RemoteConfiguration.TargetDirectory);
                        ((IRemoteZip)agent).ExtractZipFile(remoteTempPath, this.RemoteConfiguration.TargetDirectory, true);
                    }
                    else
                    {
                        LogDebug("Transferring artifact to {0}...", this.RemoteConfiguration.TargetDirectory);
                        agent.WriteFile(
                            agent.CombinePath(this.RemoteConfiguration.TargetDirectory, BaseName),
                            null,
                            null,
                            File.ReadAllBytes(tempFile),
                            false
                        );
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                LogError("Error processing the {0} artifact for build {1} of job {2}. Details: {3}", BaseName, BuildNumber, this.Job, ex.ToString());
                return false;
            }
        }

        protected void GetArtifact(string RelativePath)
        {
        }

        protected override string ProcessRemoteCommand(string name, string[] args)
        {
            throw new InvalidOperationException();
        }
    }
}
