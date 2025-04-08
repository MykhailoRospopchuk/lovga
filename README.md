# lovga

The reason for this project is to learn something new by reinventing the wheel.

# The project is built on technology
- gRPC

# The project consists of the following parts:
- **LovgaBroker** Will exist separately in a Docker container and directly perform the functions of receiving and transmitting messages
- **LovgaSatellite** A nuget package that should be installed in the target project to interact with the broker

# How to use

To use broker from docker container 

```bash
docker build . --tag broker
docker run --name "broker" -p 8080:8080 --network host broker 
```