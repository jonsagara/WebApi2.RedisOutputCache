"%systemdrive%\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" /m build.proj /t:ReleaseBuild 
nuget pack WebApi2.RedisOutputCache.nuspec -Symbols
nuget pack WebApi2.RedisOutputCache.StrongName.nuspec -Symbols
pause
