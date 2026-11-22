# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY LearnBackend.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish LearnBackend.csproj -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime (nhẹ, bảo mật)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish ./
COPY Data ./Data

ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "LearnBackend.dll"]
