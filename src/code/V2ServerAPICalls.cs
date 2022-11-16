// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.PowerShell.PowerShellGet.UtilClasses;
using System;
using System.Collections.Generic;
using System.Net.Http;
using NuGet.Versioning;
using System.Management.Automation;

namespace Microsoft.PowerShell.PowerShellGet.Cmdlets
{
    internal class V2ServerAPICalls : IServerAPICalls
    {
        /*  ******NOTE*******:
        /*  Quotations in the urls can change the response.
        /*  for example:   http://www.powershellgallery.com/api/v2/Search()?$filter=IsLatestVersion&searchTerm='az* tag:PSScript'&includePrerelease=true
        /*  will return something different than 
        /*  http://www.powershellgallery.com/api/v2/Search()?$filter=IsLatestVersion&searchTerm=az* tag:PSScript&includePrerelease=true
        /*  We believe the first example returns an "and" of the search term and the tag and the second returns "or",
        /*  this needs more investigation.
        /*  Some of the urls below may need to be modified.
        */

        // Any interface method that is not implemented here should be processed in the parent method and then call one of the implemented 
        // methods below.
        #region Members

        private static readonly HttpClient s_client = new HttpClient();
        private static readonly string select = "$select=Id,Version,Authors,Copyright,Dependencies,Description,IconUrl,IsPrerelease,Published,ProjectUrl,ReleaseNotes,Tags,LicenseUrl,CompanyName";

        #endregion

        #region Constructor

        internal V2ServerAPICalls() {}

        #endregion
        
        // http://www.powershellgallery.com/api/v2/Search()?$filter=Id eq 'PowerShellGet' &includePrerelease=true

        #region Methods
        // High level design: Find-PSResource >>> IFindPSResource (loops, version checks, etc.) >>> IServerAPICalls (call to repository endpoint/url)    

        /// <summary>
        /// Find method which allows for searching for all packages from a repository and returns latest version for each.
        /// Examples: Search -Repository PSGallery
        /// API call: 
        /// - No prerelease: http://www.powershellgallery.com/api/v2/Search()?$filter=IsLatestVersion
        /// </summary>
        public string FindAllWithNoPrerelease(PSRepositoryInfo repository, out string errRecord) {
            var requestUrlV2 = $"{repository.Uri}/Search()?$filter=IsLatestVersion";

            return HttpRequestCall(requestUrlV2, out errRecord);  
        }

        /// <summary>
        /// Find method which allows for searching for all packages from a repository and returns latest version for each.
        /// Examples: Search -Repository PSGallery
        /// API call: 
        /// - Include prerelease: http://www.powershellgallery.com/api/v2/Search()?$filter=IsAbsoluteLatestVersion&includePrerelease=true
        /// </summary>
        public string FindAllWithPrerelease(PSRepositoryInfo repository, out string errRecord) {
            var requestUrlV2 = $"{repository.Uri}/Search()?$filter=IsAbsoluteLatestVersion&includePrerelease=true";

            return HttpRequestCall(requestUrlV2, out errRecord);  
        }


        /// <summary>
        /// Find method which allows for searching for packages with tag from a repository and returns latest version for each.
        /// Examples: Search -Tag "JSON" -Repository PSGallery
        /// API call: 
        /// - Include prerelease: http://www.powershellgallery.com/api/v2/Search()?$filter=IsAbsoluteLatestVersion&searchTerm=tag:JSON&includePrerelease=true
        /// </summary>
        public string[] FindTag(string tag, PSRepositoryInfo repository, bool includePrerelease, ResourceType type, out string errRecord)
        {
            // scenarios with type + tags:
            // type: None -> search both endpoints
            // type: M -> just search Module endpoint
            // type: S -> just search Scripts end point
            // type: DSCResource -> just search Modules
            // type: Command -> just search Modules
            errRecord = String.Empty;
            var prereleaseFilter = includePrerelease ? "&includePrerelease=true" : string.Empty;
            List<string> responses = new List<string>();
            
            if (type == ResourceType.Script || type == ResourceType.None)
            {
                // $"{repository.Uri}/items/psscript/Search()?$filter=IsAbsoluteLatestVersion&searchTerm='tag:{tag}+tag:PSScript'{prereleaseFilter}&{select}";
                var scriptsRequestUrlV2 = $"{repository.Uri}/items/psscript/Search()?$filter=IsAbsoluteLatestVersion&searchTerm='tag:{tag}'{prereleaseFilter}&{select}";
                responses.Add(HttpRequestCall(requestUrlV2: scriptsRequestUrlV2, out string scriptErrorRecord));
                // TODO: add error handling here
            }

            if (type != ResourceType.Script)
            {
                // type: Module or Command or DSCResource or None
                var modulesRequestUrlV2 = $"{repository.Uri}/Search()?$filter=IsAbsoluteLatestVersion&searchTerm='tag:{tag}'{prereleaseFilter}&{select}";
                responses.Add(HttpRequestCall(requestUrlV2: modulesRequestUrlV2, out string moduleErrorRecord));
                // TODO: add error handling here
            }

            return responses.ToArray();  
        }

        public string FindCommandOrDscResource(string tag, PSRepositoryInfo repository, bool includePrerelease, bool isSearchingForCommands, out string errRecord)
        {
            // type: DSCResource -> just search Modules
            var prereleaseFilter = includePrerelease ? "&includePrerelease=true" : string.Empty;
            var tagFilter = isSearchingForCommands ? "PSCommand_" : "PSDscResource_";
            var requestUrlV2 = $"{repository.Uri}/Search()?$filter=IsAbsoluteLatestVersion&searchTerm='tag:{tagFilter}{tag}'{prereleaseFilter}&{select}";

            return HttpRequestCall(requestUrlV2, out errRecord);  
        }


        /// <summary>
        /// Find method which allows for searching for packages with resource type specified from a repository and returns latest version for each.
        /// Name: supports wildcards
        /// Type: Module, Script, Command, DSCResource (can take multiple)
        /// Examples: Search -Type Module -Repository PSGallery
        ///           Search -Type Module -Name "Az*" -Repository PSGallery
        /// TODO: discuss consolidating Modules and Scripts endpoints (move scripts to modules endpoint) ***
        /// TODO Note: searchTerm is tokenized by whitespace.
        /// - Include prerelease: http://www.powershellgallery.com/api/v2/Search()?$filter=IsLatestVersion&searchTerm='az* tag:PSScript'&includePrerelease=true
        /// </summary>
        public string FindTypesWithPrerelease(ResourceType packageResourceType, string packageName, PSRepositoryInfo repository, out string errRecord)
        {
            // There are quotations around search term and tag(s) in the url since this should be an "and" operation
            var requestUrlV2 = $"{repository.Uri}/Search()?$filter=IsLatestVersion&searchTerm='{packageName} tag:{packageResourceType}'&includePrerelease=true";

            return HttpRequestCall(requestUrlV2, out errRecord);  
        }

        /// <summary>
        /// Find method which allows for searching for command names and/or DSC resources and returns latest version of matching packages.
        /// Name: supports wildcards.
        /// Examples: Search -Name "Command1", "Command2" -Repository PSGallery
        /// Examples: Search -Name "DSCResource1", "DSCResource2" -Repository PSGallery
        /// - No prerelease: http://www.powershellgallery.com/api/v2/Search()?$filter=IsLatestVersion&searchTerm=tag:PSCommand_Command1 PSCommand_Command2
        /// </summary>
        public string FindCommandNameWithNoPrerelease(string[] commandNames, PSRepositoryInfo repository, out string errRecord) {
            var commandNamesString = String.Join(" ", commandNames);
            // There are no quotations around tag(s) in the url because this should be an "or" operation
            var requestUrlV2 = $"{repository.Uri}/Search()?$filter=IsLatestVersion&searchTerm=tag:{commandNamesString}";

            return HttpRequestCall(requestUrlV2, out errRecord);  
        }

        /// <summary>
        /// Find method which allows for searching for command names and/or DSC resources and returns latest version of matching packages.
        /// Name: supports wildcards.
        /// Examples: Search -Name "Command1", "Command2" -Repository PSGallery
        /// Examples: Search -Name "DSCResource1", "DSCResource2" -Repository PSGallery
        /// - Include prerelease: http://www.powershellgallery.com/api/v2/Search()?$filter=IsLatestVersion&searchTerm=tag:PSCommand_Command1 PSCommand_Command2&includePrerelease=true
        /// </summary>
        public string FindCommandNameWithPrerelease(string[] commandNames, PSRepositoryInfo repository, out string errRecord)
        {
            var commandNamesString = String.Join(" ", commandNames);
            // There are no quotations around tag(s) in the url because this should be an "or" operation
            var requestUrlV2 = $"{repository.Uri}/Search()?$filter=IsLatestVersion&searchTerm=tag:{commandNamesString}&includePrerelease=true";

            return HttpRequestCall(requestUrlV2, out errRecord);  
        }

        /// <summary>
        /// Find method which allows for searching for single name and returns latest version.
        /// Name: no wildcard support
        /// Examples: Search "PowerShellGet"
        /// API call: 
        /// - No prerelease: http://www.powershellgallery.com/api/v2/FindPackagesById()?id='PowerShellGet'
        /// - Include prerelease: http://www.powershellgallery.com/api/v2/FindPackagesById()?id='PowerShellGet'
        /// Implementation Note: Need to filter further for latest version (prerelease or non-prerelease dependening on user preference)
        /// </summary>
        /// // TODO:  change repository from string to PSRepositoryInfo
        public string FindName(string packageName, PSRepositoryInfo repository, bool includePrerelease, ResourceType type, out string errRecord)
        {
            // Make sure to include quotations around the package name
            var prerelease = includePrerelease ? "IsAbsoluteLatestVersion" : "IsLatestVersion";

            // This should return the latest stable version or the latest prerelease version (respectively)
            //var requestUrlV2 = $"{repository.Uri.ToString()}/FindPackagesById()?id='{packageName}'?&$orderby=Version desc&$filter={prerelease}&{select}";



            // https://www.powershellgallery.com/api/v2/FindPackagesById()?id='PowerShellGet'&$filter=IsLatestVersion and substringof('PSModule', Tags) eq true
            string typeFilterPart = type == ResourceType.None ? $" and Id eq '{packageName}'" :  $" and substringof('PS{type.ToString()}', Tags) eq true";
            var requestUrlV2 = $"{repository.Uri.ToString()}/FindPackagesById()?id='{packageName}'&$filter={prerelease}{typeFilterPart}&{select}";

            return HttpRequestCall(requestUrlV2, out errRecord);  
        }

        /// <summary>
        /// Find method which allows for searching for single name with wildcards and returns latest version.
        /// Name: supports wildcards
        /// Examples: Search "PowerShell*"
        /// API call: 
        /// - No prerelease: http://www.powershellgallery.com/api/v2/Search()?$filter=IsLatestVersion&searchTerm='az*'
        /// Implementation Note: filter additionally and verify ONLY package name was a match.
        /// </summary>
        public string FindNameGlobbing(string packageName, PSRepositoryInfo repository, bool includePrerelease, ResourceType type, out string errRecord)
        {
            // https://www.powershellgallery.com/api/v2/Search()?$filter=endswith(Id, 'Get') and startswith(Id, 'PowerShell') and IsLatestVersion (stable)
            // https://www.powershellgallery.com/api/v2/Search()?$filter=endswith(Id, 'Get') and IsAbsoluteLatestVersion&includePrerelease=true
            // Useful links: https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-odata/505b6322-c57f-4c37-94ef-daf8b6e2abd3
            // https://github.com/NuGet/Home/wiki/Filter-OData-query-requests
            
            string extraParam = "&$orderby=Id desc&$inlinecount=allpages&$skip=0&$top=6000";
            var prerelease = includePrerelease ? "IsAbsoluteLatestVersion&includePrerelease=true" : "IsLatestVersion";
            var nameFilter = string.Empty;

            var names = packageName.Split(new char[] {'*'}, StringSplitOptions.RemoveEmptyEntries);

            if (names.Length == 0)
            {
                errRecord = "We don't support -Name *";
                return string.Empty;
            }
            if (names.Length == 1)
            {
                if (packageName.StartsWith("*") && packageName.EndsWith("*"))
                {
                    // *get*
                    nameFilter = $"substringof('{names[0]}', Id)";
                }
                else if (packageName.EndsWith("*"))
                {
                    // PowerShell*
                    nameFilter = $"startswith(Id, '{names[0]}')";
                }
                else
                {
                    // *ShellGet
                    nameFilter = $"endswith(Id, '{names[0]}')";
                }
            }
            else if (names.Length == 2 && !packageName.StartsWith("*") && !packageName.EndsWith("*"))
            {
                // *pow*get*
                // pow*get -> only support this
                // pow*get*
                // *pow*get
                nameFilter = $"startswith(Id, '{names[0]}') and endswith(Id, '{names[1]}')";
            }
            else 
            {
                errRecord = "We only support wildcards for scenarios similar to the following examples: PowerShell*, *ShellGet, Power*Get, *Shell*.";
                return string.Empty;
            }
            
            var requestUrlV2 = $"{repository.Uri.ToString()}/Search()?$filter={nameFilter} and {prerelease}&{select}{extraParam}";
            
            return HttpRequestCall(requestUrlV2, out errRecord);  
        }

        /// <summary>
        /// Find method which allows for searching for single name with version range.
        /// Name: no wildcard support
        /// Version: supports wildcards
        /// Examples: Search "PowerShellGet" "[3.0.0.0, 5.0.0.0]"
        ///           Search "PowerShellGet" "3.*"
        /// API Call: http://www.powershellgallery.com/api/v2/FindPackagesById()?id='PowerShellGet'
        /// Implementation note: Returns all versions, including prerelease ones. Later (in the API client side) we'll do filtering on the versions to satisfy what user provided.
        /// </summary>
        public string FindVersionGlobbing(string packageName, VersionRange versionRange, PSRepositoryInfo repository, bool includePrerelease, ResourceType type, out string errRecord)
        {
            //https://www.powershellgallery.com/api/v2//FindPackagesById()?id='blah'&includePrerelease=false&$filter= NormalizedVersion gt '1.0.0' and NormalizedVersion lt '2.2.5' and substringof('PSModule', Tags) eq true 



            //https://www.powershellgallery.com/api/v2//FindPackagesById()?id='PowerShellGet'&includePrerelease=false&$filter= NormalizedVersion gt '1.1.1' and NormalizedVersion lt '2.2.5'
            // NormalizedVersion doesn't include trailing zeroes
            // Notes: this could allow us to take a version range (i.e (2.0.0, 3.0.0.0]) and deconstruct it and add options to the Filter for Version to describe that range
            // will need to filter additionally, if IncludePrerelease=false, by default we get stable + prerelease both back
            // Current bug: Find PSGet -Version "2.0.*" -> https://www.powershellgallery.com/api/v2//FindPackagesById()?id='PowerShellGet'&includePrerelease=false&$filter= Version gt '2.0.*' and Version lt '2.1'
            // Make sure to include quotations around the package name
            
            //and IsPrerelease eq false
            // ex:
            // (2.0.0, 3.0.0)
            // $filter= NVersion gt '2.0.0' and NVersion lt '3.0.0'

            // [2.0.0, 3.0.0]
            // $filter= NVersion ge '2.0.0' and NVersion le '3.0.0'

            // [2.0.0, 3.0.0)
            // $filter= NVersion ge '2.0.0' and NVersion lt '3.0.0'

            // (2.0.0, 3.0.0]
            // $filter= NVersion gt '2.0.0' and NVersion le '3.0.0'

            // [, 2.0.0]
            // $filter= NVersion le '2.0.0'

            string format = "NormalizedVersion {0} {1}";
            string minPart = String.Empty;
            string maxPart = String.Empty;

            if (versionRange.MinVersion != null)
            {
                string operation = versionRange.IsMinInclusive ? "ge" : "gt";
                minPart = String.Format(format, operation, $"'{versionRange.MinVersion.ToNormalizedString()}'");
            }

            if (versionRange.MaxVersion != null)
            {
                string operation = versionRange.IsMaxInclusive ? "le" : "lt";
                maxPart = String.Format(format, operation, $"'{versionRange.MaxVersion.ToNormalizedString()}'");
            }

            string versionFilterParts = String.Empty;
            if (!String.IsNullOrEmpty(minPart) && !String.IsNullOrEmpty(maxPart))
            {
                versionFilterParts += minPart + " and " + maxPart;
            }
            else if (!String.IsNullOrEmpty(minPart))
            {
                versionFilterParts += minPart;
            }
            else if (!String.IsNullOrEmpty(maxPart))
            {
                versionFilterParts += maxPart;
            }

            string filterQuery = "&$filter=";
            filterQuery += includePrerelease ? string.Empty : "IsPrerelease eq false";
            //filterQuery +=  type == ResourceType.None ? String.Empty : $" and substringof('PS{type.ToString()}', Tags) eq true";

            string joiningOperator = filterQuery.EndsWith("=") ? String.Empty : " and " ;
            filterQuery += type == ResourceType.None ? String.Empty : $"{joiningOperator}substringof('PS{type.ToString()}', Tags) eq true";

            if (!String.IsNullOrEmpty(versionFilterParts))
            {
                // Check if includePrerelease is true, if it is we want to add "$filter"
                // Single case where version is "*" (or "[,]") and includePrerelease is true, then we do not want to add "$filter" to the requestUrl.
        
                // Note: could be null/empty if Version was "*" -> [,]
                joiningOperator = filterQuery.EndsWith("=") ? String.Empty : " and " ;
                filterQuery +=  $"{joiningOperator}{versionFilterParts}";
            }

            var requestUrlV2 = $"{repository.Uri.ToString()}/FindPackagesById()?id='{packageName}'&$orderby=NormalizedVersion desc&{select}{filterQuery}";

            return HttpRequestCall(requestUrlV2, out errRecord);  
        }

        /// <summary>
        /// Find method which allows for searching for single name with specific version.
        /// Name: no wildcard support
        /// Version: no wildcard support
        /// Examples: Search "PowerShellGet" "2.2.5"
        /// API call: http://www.powershellgallery.com/api/v2/Packages(Id='PowerShellGet', Version='2.2.5')
        /// </summary>
        public string FindVersion(string packageName, string version, PSRepositoryInfo repository, ResourceType type, out string errRecord) {
            // https://www.powershellgallery.com/api/v2//FindPackagesById()?id='blah'&includePrerelease=false&$filter= NormalizedVersion eq '1.1.0' and substringof('PSModule', Tags) eq true 

            // Quotations around package name and version do not matter, same metadata gets returned.
            //var requestUrlV2 = $"{repository.Uri.ToString()}/Packages(Id='{packageName}', Version='{version}')?{select}";
            string typeFilterPart = type == ResourceType.None ? String.Empty :  $" and substringof('PS{type.ToString()}', Tags) eq true";
            var requestUrlV2 = $"{repository.Uri.ToString()}/FindPackagesById()?id='{packageName}'&$filter= NormalizedVersion eq '{version}'{typeFilterPart}&{select}";
            
            return HttpRequestCall(requestUrlV2, out errRecord);  
        }

        /**  INSTALL APIS **/

        /// <summary>
        /// Installs specific package.
        /// Name: no wildcard support.
        /// Examples: Install "PowerShellGet"
        /// Implementation Note:   if not prerelease: https://www.powershellgallery.com/api/v2/package/powershellget (Returns latest stable)
        ///                        if prerelease, the calling method should first call IFindPSResource.FindName(), 
        ///                             then find the exact version to install, then call into install version
        /// </summary>
        public string InstallName(string packageName, PSRepositoryInfo repository, out string errRecord) {
            var requestUrlV2 = $"{repository.Uri.ToString()}/package/{packageName}";

            // The request returns a byte array, so think about what we want to return here
            // ACR code to read stream may be helpful here. 
            return HttpRequestCall(requestUrlV2, out errRecord);  
        }


        /// <summary>
        /// Installs package with specific name and version.
        /// Name: no wildcard support.
        /// Version: no wildcard support.
        /// Examples: Install "PowerShellGet" -Version "3.0.0.0"
        ///           Install "PowerShellGet" -Version "3.0.0-beta16"
        /// API Call: https://www.powershellgallery.com/api/v2/package/Id/version (version can be prerelease)
        /// </summary>    
        public string InstallVersion(string packageName, NuGetVersion version, PSRepositoryInfo repository, out string errRecord) {
            var requestUrlV2 = $"{repository.Uri.ToString()}/package/{packageName}/{version}";

            // The request returns a byte array, so think about what we want to return here
            // ACR code to read stream may be helpful here. 
            return HttpRequestCall(requestUrlV2, out errRecord); 
        }


        private static string HttpRequestCall(string requestUrlV2, out string errRecord) {
            errRecord = string.Empty;

            // request object will send requestUrl 
            try
                {
                    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUrlV2);
                    
                    // We can have this return a Task, or the response (json string)
                    var response = Utils.SendV2RequestAsync(request, s_client).GetAwaiter().GetResult();

                    // Do we want to check if response is 200?
                    // response will be json metadata object that will get returned
                    return response.ToString();
                }
                catch (HttpRequestException e)
                {
                    errRecord = "Error occured while trying to retrieve response: " + e.Message;
                }

            return string.Empty;
        }
        #endregion
    }
}