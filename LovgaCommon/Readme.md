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

Add nuget and reference to protobuf files from nuget in .csproj file
It is necessary to add flag **GeneratePathProperty="true"** (by default it is not included)
```xml
<ItemGroup>
    <PackageReference Include="LovgaCommon" Version="1.0.*" GeneratePathProperty="true"/>
</ItemGroup>
```
```xml
<ItemGroup>
    <Protobuf Include="$(PkgLovgaCommon)\buildTransitive\Protos\subscriber.proto" GrpcServices="*"/>
    <Protobuf Include="$(PkgLovgaCommon)\buildTransitive\Protos\consumer.proto" GrpcServices="*"/>
    <Protobuf Include="$(PkgLovgaCommon)\buildTransitive\Protos\publisher.proto" GrpcServices="*"/>
    <Protobuf Include="$(PkgLovgaCommon)\buildTransitive\Protos\common.proto" />
</ItemGroup>
```