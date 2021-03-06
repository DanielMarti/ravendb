param(    
    [string]$url = $(throw "-url is required."), 
    [string]$dest = $(throw "-dest is required.")
    );


$json = '{ "BackupLocation": "' + $dest.Replace("\","\\") + '"}'

$req = [System.Net.WebRequest]::Create($url + "/admin/backup");
$req.Method = "POST"
$req.UseDefaultCredentials = $true
$req.PreAuthenticate = $true
$req.Credentials = [System.Net.CredentialCache]::DefaultCredentials
$streamWriter = new-object System.IO.StreamWriter($req.GetRequestStream())
$streamWriter.WriteLine($json)
$streamWriter.Flush()
$streamWriter.Dispose();

$resp = $req.GetResponse()
Write-Host $resp.StatusCode 
