#!/bin/sh
set -eu
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DO_NOT_TRACK=1
if [ ! -f BoundedObservedSet.sln ]; then
    printf '%s\n' 'Run this script from the repository root.' >&2
    exit 2
fi
case "${1:-offline}" in
    restore)
        dotnet restore BoundedObservedSet.sln --locked-mode --disable-parallel --disable-build-servers
        ;;
    offline)
        dotnet test --solution BoundedObservedSet.sln --no-restore --configuration Release
        dotnet run --project demo/BoundedObservedSet.Demo.csproj --no-restore --configuration Release -- --self-test
        ;;
    all)
        dotnet restore BoundedObservedSet.sln --locked-mode --disable-parallel --disable-build-servers
        dotnet test --solution BoundedObservedSet.sln --no-restore --configuration Release
        dotnet run --project demo/BoundedObservedSet.Demo.csproj --no-restore --configuration Release -- --self-test
        ;;
    *)
        printf '%s\n' 'Usage: sh scripts/check.sh [restore|offline|all]' >&2
        exit 2
        ;;
esac
