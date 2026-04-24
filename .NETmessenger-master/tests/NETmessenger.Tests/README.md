# NETmessenger backend security tests

These tests use Testcontainers with PostgreSQL, so Docker Desktop must be running before `dotnet test`.

If Docker is configured with a stale local proxy, image pulls can fail with errors that mention `127.0.0.1:10801`. In that case, open Docker Desktop settings and disable or fix the proxy, then retry. The test factory also clears loopback proxy variables inside the test process so Docker endpoint detection is not routed through a dead local proxy.

Useful local checks:

```powershell
docker ps
docker pull postgres:15-alpine
dotnet test .NETmessenger-master/tests/NETmessenger.Tests/NETmessenger.Tests.csproj -m:1
```

The test factory disables Ryuk for local stability and disposes the PostgreSQL container explicitly after the test run.
