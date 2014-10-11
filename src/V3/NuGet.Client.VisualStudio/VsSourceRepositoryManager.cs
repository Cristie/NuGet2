﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NuGet.Client.Interop;
using NuGet.VisualStudio;
using System.Collections.Concurrent;

namespace NuGet.Client.VisualStudio
{
    /// <summary>
    /// Manages active source repositories using the NuGet Visual Studio settings interfaces
    /// </summary>
    [Export(typeof(SourceRepositoryManager))]
    public class VsSourceRepositoryManager : SourceRepositoryManager
    {
        private readonly IVsPackageSourceProvider _sourceProvider;
        private readonly IPackageRepositoryFactory _repoFactory;
        private readonly ConcurrentDictionary<string, SourceRepository> _repos = new ConcurrentDictionary<string, SourceRepository>();

        public override SourceRepository ActiveRepository
        {
            get
            {
                Debug.Assert(!_sourceProvider.ActivePackageSource.IsAggregate(), "Active source is the aggregate source! This shouldn't happen!");

                return GetRepo(new PackageSource(
                    _sourceProvider.ActivePackageSource.Name,
                    _sourceProvider.ActivePackageSource.Source));
            }
        }

        public override SourceRepository CreateSourceRepository(PackageSource packageSource)
        {
            return GetRepo(packageSource);
        }

        public override IEnumerable<PackageSource> AvailableSources
        {
            get
            {
                return _sourceProvider
                    .GetEnabledPackageSources()
                    .Select(
                        s => new PackageSource(s.Name, s.Source));
            }
        }

        [ImportingConstructor]
        public VsSourceRepositoryManager(IVsPackageSourceProvider sourceProvider, IPackageRepositoryFactory repoFactory)
        {
            _sourceProvider = sourceProvider;
            _sourceProvider.PackageSourcesSaved += (sender, e) =>
            {
                if (PackageSourcesChanged != null)
                {
                    PackageSourcesChanged(this, EventArgs.Empty);
                }
            };
            _repoFactory = repoFactory;
        }

        public override void ChangeActiveSource(PackageSource newSource)
        {
            var source = _sourceProvider.GetEnabledPackageSources()
                .FirstOrDefault(s => String.Equals(s.Name, newSource.Name, StringComparison.OrdinalIgnoreCase));
            if (source == null)
            {
                throw new ArgumentException(
                    String.Format(
                        CultureInfo.CurrentCulture,
                        Strings.VsPackageManagerSession_UnknownSource,
                        newSource.Name),
                    "newSource");
            }

            // The Urls should be equal but if they aren't, there's nothing the user can do about it :(
            Debug.Assert(String.Equals(source.Source, newSource.Url, StringComparison.Ordinal));

            // Update the active package source
            _sourceProvider.ActivePackageSource = source;
        }

        public override event EventHandler PackageSourcesChanged;
        private SourceRepository GetRepo(PackageSource p)
        {
            return _repos.GetOrAdd(p.Url, _ => CreateRepo(p));
        }

        private SourceRepository CreateRepo(PackageSource source)
        {
            // For now, be awful. Detect V3 via the source URL itself
            Uri url;
            if (Uri.TryCreate(source.Url, UriKind.RelativeOrAbsolute, out url) &&
                url.Host.Equals("preview.nuget.org", StringComparison.OrdinalIgnoreCase))
            {
                return new V3SourceRepository(source);
            }

            return new V2SourceRepository(source, _repoFactory.CreateRepository(source.Url));
        }
    }
}