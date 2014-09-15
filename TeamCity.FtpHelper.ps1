function escapeTeamCityBuildOutput ([string] $value) {
	$value = $value.Replace("|", "||").Replace("'", "|'").Replace('`n', '|`n').Replace('`r', '|`r').Replace("[", "|[").Replace("]", "|]");
                
    return ($value);
}

function writeTeamCityMessage ($message, $errorDetails, $status) {
    $message = escapeTeamCityBuildOutput($message)
	$output = "##teamcity[message text='" + $message + "'";
    
    if($errorDetails -ne $null){
        $errorDetails = escapeTeamCityBuildOutput($errorDetails);
        $output = $output + " errorDetails='" + $errorDetails + "'";
    }
    
    $output = $output + " status='" + $status + "']";
    
    Write-Host $output;
}

