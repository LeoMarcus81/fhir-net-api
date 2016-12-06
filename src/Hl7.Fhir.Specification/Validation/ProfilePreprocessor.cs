﻿/* 
 * Copyright (c) 2016, Furore (info@furore.com) and contributors
 * See the file CONTRIBUTORS for details.
 * 
 * This file is licensed under the BSD 3-Clause license
 * available at https://raw.githubusercontent.com/ewoutkramer/fhir-net-api/master/LICENSE
 */

using Hl7.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification.Navigation;
using Hl7.Fhir.Support;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Hl7.Fhir.Validation
{
    internal class ProfilePreprocessor
    {
        private Func<string, StructureDefinition> _profileResolver;
        private Action<StructureDefinition> _snapshotGenerator;
        private string _path;
        private ProfileAssertion _profiles;

        public ProfilePreprocessor(Func<string,StructureDefinition> profileResolver, Action<StructureDefinition> snapshotGenerator, 
                IElementNavigator instance, string declaredTypeProfile, 
                IEnumerable<StructureDefinition> additionalProfiles, IEnumerable<string> additionalCanonicals)
        {
            _profileResolver = profileResolver;
            _snapshotGenerator = snapshotGenerator;
            _path = instance.Path;

            _profiles = new ProfileAssertion(_path, _profileResolver);

            if (instance.TypeName != null) _profiles.SetInstanceType(ModelInfo.CanonicalUriForFhirCoreType(instance.TypeName));
            if (declaredTypeProfile != null) _profiles.SetDeclaredType(declaredTypeProfile);

            // This is only for resources, but I don't bother checking, since this will return empty anyway
            _profiles.AddStatedProfile(instance.GetChildrenByName("meta").ChildrenValues("profile").Cast<string>());
                     
            if(additionalProfiles != null) _profiles.AddStatedProfile(additionalProfiles);
            if(additionalCanonicals != null) _profiles.AddStatedProfile(additionalCanonicals);
        }

        public IEnumerable<ElementDefinitionNavigator> Result { get; private set; }


        public OperationOutcome Process()
        {
            var outcome = new OperationOutcome();

            // Start preprocessing by resolving the references to the profiles (if any)
            var resolveOutcome = _profiles.Resolve();
            outcome.Add(resolveOutcome);

            if (resolveOutcome.Success)
            {
                // Then, validate consistency of the profile assertions
                var validationOutcome = _profiles.Validate();
                outcome.Add(validationOutcome);

                if (validationOutcome.Success)
                {
                    if (_profiles.MinimalProfiles.Any())
                    {

                        // Then, generate snapshots for all sds that we have found
                        var genSnapshotOutcome = GenerateSnapshots(_profiles.MinimalProfiles, _snapshotGenerator, _path);
                        outcome.Add(genSnapshotOutcome);

                        if (genSnapshotOutcome.Success)
                        {
                            // Finally, return navigators to the definitions
                            Result = CreateNavigators(_profiles.MinimalProfiles);
                        }
                    }
                    else
                        outcome.AddIssue("There are no profile and type assertions at this point in the instance, so validation cannot succeed",
                                Issue.PROFILE_NO_PROFILE_TO_VALIDATE_AGAINST, _path);

                }
            }

            return outcome;
        }


        /// <summary>
        /// Generate snapshots for all StructureDefinitions available to the preprocessor
        /// </summary>
        /// <returns></returns>
        public static OperationOutcome GenerateSnapshots(IEnumerable<StructureDefinition> sds, Action<StructureDefinition> snapshotGenerator, string path)
        {
            var outcome = new OperationOutcome();

            foreach (var sd in sds)
            {
                if (!sd.HasSnapshot)
                {
                    try
                    {
                        snapshotGenerator(sd);
                    }
                    catch (Exception e)
                    {
                        outcome.AddIssue($"Snapshot generation failed for '{sd.Url}'. Message: {e.Message}",
                               Issue.UNAVAILABLE_SNAPSHOT_GENERATION_FAILED, path);
                    }
                }

                if (!sd.HasSnapshot)
                    outcome.AddIssue($"Profile '{sd.Url}' does not include a snapshot.", Issue.UNAVAILABLE_NEED_SNAPSHOT, path);
            }

            return outcome;
        }



        /// <summary>
        /// Generate navigators for all StructureDefinitions with snapshots available to the preprocessor
        /// </summary>
        /// <returns></returns>
        public static List<ElementDefinitionNavigator> CreateNavigators(IEnumerable<StructureDefinition> sds)
        {
            return sds.Where(sd => sd.HasSnapshot).Select(sd => new ElementDefinitionNavigator(sd)).ToList();
        }
    }
}