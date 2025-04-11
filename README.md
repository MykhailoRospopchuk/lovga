# lovga

The reason for this project is to learn something new by reinventing the wheel.

# The project is built on technology
- gRPC

# The project consists of the following parts:
- **LovgaBroker** Will exist separately in a Docker container and directly perform the functions of receiving and transmitting messages
- **LovgaSatellite** A nuget package that should be installed in the target project to interact with the broker

# How to use

To use broker from docker container
1. The folder tree should look like this:
```bash
./lovga
├── .local_nuget
├── .storage_database
├── LovgaBroker
├── LovgaClient
├── LovgaCommon
├── LovgaPublisher
├── LovgaSatellite
└── TestProject
```
2. Create nuget from LovgaCommon inside .local_nuget directory near docker file.
3. Prepare docker container and run:
```bash
    docker build . --tag broker
    docker run --name "broker" -p 8080:8080 --network="host" -v /home/mykhailo/dev/pet/lovga/lovga/.storage_database:/.storage_database broker
```
*/home/mykhailo/dev/pet/lovga* - you must put your own path

