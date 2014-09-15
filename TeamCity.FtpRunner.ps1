$currentDir = Split-Path -parent $MyInvocation.MyCommand.Definition
. ($currentDir + "\TeamCity.FtpHelper.ps1"); 

[System.Reflection.Assembly]::LoadWithPartialName("System.Xml.Linq") | Out-Null;

$ftpHelperDefinition = [System.IO.File]::ReadAllText('FtpHelper.cs');
Add-Type -Language CSharp $ftpHelperDefinition;

$isTeamCity = Test-Path Env:\BUILD_NUMBER;
if ($isTeamCity -eq $true) {
    $buildConfFile = (Get-Item Env:\TEAMCITY_BUILD_PROPERTIES_FILE).Value;
} else {
    $buildConfFile = 'C:\Users\Rahul Singla\Desktop\teamcity\teamcity.build8947796857307948281.properties - Copy.xml';
}

$buildPropertiesDoc = [System.Xml.Linq.XDocument]::Parse([System.IO.File]::ReadAllText($buildConfFile));
$changedFilePath = [System.Xml.XPath.Extensions]::XPathSelectElement($buildPropertiesDoc, "/properties/entry[@key='teamcity.build.changedFiles.file']").Value;
$changedFileInfo = New-Object System.IO.FileInfo ($changedFilePath);
if($changedFileInfo.Length -eq $null -or $changedFileInfo.Length -eq 0) {
    return;
}


$checkoutDir = [System.Xml.XPath.Extensions]::XPathSelectElement($buildPropertiesDoc, "/properties/entry[@key='teamcity.build.checkoutDir']").Value;
$ftpBaseUrl = [System.Xml.XPath.Extensions]::XPathSelectElement($buildPropertiesDoc, "/properties/entry[@key='ftpBaseUrl']").Value;
$ftpUsername = [System.Xml.XPath.Extensions]::XPathSelectElement($buildPropertiesDoc, "/properties/entry[@key='ftpUsername']").Value;
$ftpPassword = [System.Xml.XPath.Extensions]::XPathSelectElement($buildPropertiesDoc, "/properties/entry[@key='ftpPassword']").Value;

if([String]::IsNullOrEmpty($ftpBaseUrl)) {
    return;
}

$ftpPathMappingsEl = [System.Xml.XPath.Extensions]::XPathSelectElement($buildPropertiesDoc, "/properties/entry[@key='ftpPathMappings']");
if($ftpPathMappingsEl -ne $null) {
    $ftpPathMappingsLines = $ftpPathMappingsEl.Value.Split(@("`r`n", "`r", "`n", ";"), [System.StringSplitOptions]::RemoveEmptyEntries);
    $ftpPathMappings = New-Object "System.Collections.Generic.Dictionary[string, string]";
    
    foreach($ftpPathMappingsLine in $ftpPathMappingsLines) {
        $components = $ftpPathMappingsLine.Split(@("=>"), [System.StringSplitOptions]::RemoveEmptyEntries);
        $sourcePart = $components[0].Trim('/');
        $destPart = if($components.Length -gt 1) {$components[1].Trim('/')} else {$null};
        $ftpPathMappings[$sourcePart] = $destPart;
    }
} else {
    $ftpPathMappings = $null;
}

$checkoutDir = $checkoutDir.Replace('\', '/');
if($checkoutDir.EndsWith('/') -eq $false) {
    $checkoutDir = $checkoutDir + '/';
}
if($ftpBaseUrl.EndsWith('/') -eq $false) {
    $ftpBaseUrl = $ftpBaseUrl + '/';
}


$ftpHelper = New-Object FtpHelper.FtpHelper ($ftpBaseUrl, $ftpUsername, $ftpPassword);
$changedFiles = [System.IO.File]::ReadAllLines($changedFilePath);

foreach ($changedFile in $changedFiles)
{
    $parts = $changedFile.Split(':');
    $relativeSourcePath = $parts[0];
    $action = $parts[1];
    
    $relativeSourcePath = $relativeSourcePath.Replace('\\', '/');
    $relativeSourcePath = $relativeSourcePath.Trim('/');
    
    $resolvedSourcePath = $checkoutDir + $relativeSourcePath;
    
    $relativeDestPath = $relativeSourcePath;
    if($ftpPathMappings -ne $null -band $ftpPathMappings.Count -gt 0) {
        $found = $false;
        foreach ($pair in $ftpPathMappings.GetEnumerator()) {
            if($relativeDestPath.StartsWith($pair.Key) -eq $true) {
                #This allows a ftpPathMapping of the form /=>/ which should usually be the last ftpPathMapping if it exists.
                #Thus you can re-map specific folders and leave everything else mapped at default. If you specify ftpPathMappings but omit /=>/,
                #any changed resource which does not satisfy one of the mappings won't be processed at all thus enabling you to selectively map your repo to FTP.
                if($pair.Key.Length -gt 0) {
                    $relativeDestPath = $relativeDestPath.Replace($pair.Key, $pair.Value);
                }
                
                $found = $true;
                break;
            }
        }
        
        if($found -eq $false) {
            continue;
        }
    }

    Write-Host "##teamcity[blockOpened name=$resolvedSourcePath]";
    writeTeamCityMessage ('Processing:- ' + $resolvedSourcePath) $null 'NORMAL';
    
	try {
        switch ($action)
        { 
            "CHANGED" {
                writeTeamCityMessage ('Updating:- ' + $resolvedSourcePath + ' to ' + $relativeDestPath) $null 'NORMAL';
                $ftpHelper.UploadFile($relativeDestPath, $resolvedSourcePath);
                writeTeamCityMessage ('Updated:- ' + $resolvedSourcePath + ' to ' + $relativeDestPath) $null 'NORMAL';
            }
            
            "ADDED" {
                writeTeamCityMessage ('Adding:- ' + $resolvedSourcePath + ' to ' + $relativeDestPath) $null 'NORMAL';
                $ftpHelper.UploadFile($relativeDestPath, $resolvedSourcePath);
                writeTeamCityMessage ('Added:- ' + $resolvedSourcePath + ' to ' + $relativeDestPath) $null 'NORMAL';
            }
            
            "REMOVED" {
                writeTeamCityMessage ('Removing:- ' + $resolvedSourcePath + ' from ' + $relativeDestPath) $null 'NORMAL';
                $size = $ftpHelper.GetFileSize($relativeDestPath, $true);
                if($size -ne $null) {
                    $ftpHelper.DeleteFile($relativeDestPath);
                }
                writeTeamCityMessage ('Removed:- ' + $resolvedSourcePath + ' from ' + $relativeDestPath) $null 'NORMAL';
            }
            
            "DIRECTORY_CHANGED" {
                writeTeamCityMessage ('Directory change ignored:- ' + $resolvedSourcePath + ' to ' + $relativeDestPath) $null 'NORMAL';
            }
            
            "DIRECTORY_ADDED" {
                writeTeamCityMessage ('Creating directory:- ' + $resolvedSourcePath + ' as ' + $relativeDestPath) $null 'NORMAL';
                $ftpHelper.MakeDirectory($relativeDestPath);
                writeTeamCityMessage ('Created directory:- ' + $resolvedSourcePath + ' as ' + $relativeDestPath) $null 'NORMAL';
            }
            
            "DIRECTORY_REMOVED" {
                writeTeamCityMessage ('Removing directory:- ' + $resolvedSourcePath + ' from ' + $relativeDestPath) $null 'NORMAL';
                $ftpHelper.RemoveDirectory($relativeDestPath, $true);
                writeTeamCityMessage ('Removed directory:- ' + $resolvedSourcePath + ' from ' + $relativeDestPath) $null 'NORMAL';
            }
            
            "NOT_CHANGED" {
                writeTeamCityMessage ('Ignoring Not Changed file:- ' + $resolvedSourcePath + ' to ' + $relativeDestPath) $null 'NORMAL';
            }
            
            default {
                writeTeamCityMessage ('Received unrecognized TeamCity action:- ' + $action) $null 'WARNING';
            }
        }
        
        writeTeamCityMessage ('Finished Processing:- ' + $resolvedSourcePath) $null 'NORMAL';

	} catch [Exception] {
        writeTeamCityMessage $_.Exception.Message $_.Exception.ToString() 'ERROR';
	}

    Write-Host "##teamcity[blockClosed name=$resolvedSourcePath]";
}
