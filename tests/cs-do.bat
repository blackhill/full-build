@echo off
setlocal

call fb-init.bat

taskkill /im tgitcache.exe 1>NUL 2>NUL
if not exist cs-init call cs-init.bat || goto :ko
robocopy cs-init cs-do /MIR 

pushd cs-do
rem robocopy c:\src\Paket\bin .paket /MIR

rem %FULLBUILD% workspace index || goto :ko
rem %FULLBUILD% package install || goto :ko
rem %FULLBUILD% package simplify || goto :ko
%FULLBUILD% clone * || goto :ko
%FULLBUILD% convert || goto :ko
%FULLBUILD% add view all using * || goto :ko
%FULLBUILD% list view || goto :ko
%FULLBUILD% describe view all || goto :ko
%FULLBUILD% graph all || goto :ko
%FULLBUILD% build all || goto :ko

pushd cassandra-sharp
git add --all
git commit -am "after conversion"
popd

pushd cassandra-sharp-contrib
git add --all
git commit -am "after conversion"
popd

%FULLBUILD% bookmark || goto :ko



rem msbuild csc.sln
rem msbuild cs.sln

rem %FULLBUILD% view drop cs || goto :ko
rem %FULLBUILD% view list || goto :ko

rem %FULLBUILD% index workspace || goto :ko
rem %FULLBUILD% install packages || goto :ko
rem %FULLBUILD% optimize workspace || goto :ko
rem copy ..\Template.csproj .full-build || goto :ko
rem %FULLBUILD% convert projects || goto :ko
rem %FULLBUILD% init view cs with cassandra-sharp cassandra-sharp-contrib || goto :ko
rem %FULLBUILD% generate view cs || goto :ko
rem %FULLBUILD% build view cs || goto :ko
rem %FULLBUILD% exec "echo %%FULLBUILD_REPO%% & git log -n 1 && echo." || goto :ko
rem %FULLBUILD% exec "git status" || goto :ko
rem %FULLBUILD% check packages || goto :ko
rem %FULLBUILD% use package moq version * || goto :ko
rem %FULLBUILD% check packages || goto :ko
rem %FULLBUILD% upgrade packages || goto :ko
rem %FULLBUILD% check packages || goto :ko
rem %FULLBUILD% list packages || goto :ko
rem %FULLBUILD% list nugets || goto :ko
rem %FULLBUILD% describe view cs || goto :ko
rem %FULLBUILD% list views || goto :ko
rem %FULLBUILD% drop view cs || goto :ko
rem %FULLBUILD% list views || goto :ko
rem %FULLBUILD% bookmark workspace || goto :ko


:ok
echo *** SUCCESSFUL
exit /b 0

:ko
echo *** FAILURE
exit /b 0
