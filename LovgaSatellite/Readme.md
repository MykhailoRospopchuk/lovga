to create nugget locally
```
dotnet pack --output ../.local_nuget/
```

To install common nuget need add source to nuget.config in project folder
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
    <packageSources>
        <add key="lovga_local_nuget" value="../.local_nuget/" />
    </packageSources>
</configuration>
```