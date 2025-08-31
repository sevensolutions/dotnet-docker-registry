# 🐳 .NET Docker Registry

A very simple reference implementation of the Docker V2 HTTP API using .NET and Minimal APIs.
It supports `docker push` and `docker pull` commands.
Uploaded artifacts are stored in any S3-compatible backend.

## Getting Started

1. Run a local MinIO instance.
2. Start the server with `dotnet run`.
3. Pull an image with `docker pull alpine`.
4. Retag the image with `docker tag alpine 10.0.1.55:5001/alpine` (where 10.0.1.55 is the IP address where the application is exposed).
5. Push the image with `docker push 10.0.1.55:5001/alpine`.
6. Pull the image with `docker pull 10.0.1.55:5001/alpine`.

## Special Thanks

Special thanks to [Octopus Deploy](https://octopus.com) for their very helpful blog article ❤️:  
https://octopus.com/blog/custom-docker-registry

## Useful Links

- https://docker-docs.uclv.cu/registry/spec/api/
- https://distribution.github.io/distribution/spec/manifest-v2-2/
- https://www.digitalocean.com/blog/inside-container-registry-mechanics-of-push-pull
- https://docs.docker.com/reference/api/registry/auth/