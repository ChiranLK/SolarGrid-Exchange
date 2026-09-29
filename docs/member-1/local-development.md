# Running the API locally for web and Android testing

If the Android app shows **"Unable to reach the SolarGrid API. Check the connection and API address."**,
the API is not running on `http://localhost:5076`, or it stopped at start-up because configuration is missing.
The debug Android build calls `http://10.0.2.2:5076/api/`, which is the emulator's address for the host PC.

## 1. Local MongoDB (Docker)

A dedicated single-node replica set, same image and mode as `scripts/run-component3-tests.ps1`:

```powershell
docker run --detach --name solargrid-dev-mongo --restart unless-stopped `
  --publish 127.0.0.1:27019:27019 --volume solargrid-dev-mongo-data:/data/db `
  mongo:8.0 mongod --replSet rs0 --bind_ip_all --port 27019
docker exec solargrid-dev-mongo mongosh --quiet --port 27019 `
  --eval "rs.initiate({_id:'rs0',members:[{_id:0,host:'localhost:27019'}]})"
```

## 2. Local secrets (stored outside the repository)

```powershell
dotnet user-secrets --project SolarMicrogrid.API set "MongoSettings:ConnectionString" "mongodb://localhost:27019/?replicaSet=rs0&directConnection=true"
dotnet user-secrets --project SolarMicrogrid.API set "JwtSettings:Key" "<random value of at least 32 characters>"
```

Generate the key locally (for example `openssl rand -base64 48`). Never commit it, and never reuse a key
that has appeared in Git history.

## 3. Start the API on the port the Android emulator uses

```powershell
dotnet run --project SolarMicrogrid.API --launch-profile http
```

Check `http://localhost:5076/health`: both `api` and `database` should be `Healthy`.

## 4. First Backoffice account (to activate registered Prosumers)

Follow `docs/member-1/backoffice-bootstrap.md` (environment variables, disabled by default).

## Clean up

```powershell
docker rm -f solargrid-dev-mongo
docker volume rm solargrid-dev-mongo-data
```
