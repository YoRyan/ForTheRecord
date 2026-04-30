FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src
COPY . .
RUN dotnet restore ForTheRecord/ForTheRecord.fsproj
RUN dotnet publish ForTheRecord/ForTheRecord.fsproj -c release -o /app --no-self-contained --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0

RUN groupadd -r ftr && useradd --no-log-init -r -g ftr ftr
USER ftr
WORKDIR /app
COPY --from=build /app .

ENTRYPOINT ["./ForTheRecord"]