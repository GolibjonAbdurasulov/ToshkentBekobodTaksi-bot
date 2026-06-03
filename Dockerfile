FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY Toshkent_Bekobod_Taksi/*.csproj .
RUN dotnet restore

COPY Toshkent_Bekobod_Taksi/ .
RUN dotnet publish -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /out .

EXPOSE 80

ENV ASPNETCORE_URLS=http://0.0.0.0:\${PORT:-80}

CMD ["dotnet", "Toshkent_Bekobod_Taksi.dll"]
