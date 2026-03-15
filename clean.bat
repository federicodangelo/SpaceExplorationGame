FOR /D /R . %%d IN (bin,obj) DO @IF EXIST "%%d" RD /S /Q "%%d"
