$nuget_password=$args[0]

docker build . --tag kompaz-web-backend:develop `
	--file src/Presentation/Dockerfile `
	--build-arg NUGET_PASSWORD=$nuget_password
