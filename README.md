# GetDirSyncChanges
Track changes in AD in real time

This code will connect to a specified domain controller and then show changes that are happening in AD when either a change is made directly on the specified domain controller or when replication  happens from another domain controller to the specified domain controller.

## How to use

You can just run the executable without any commandline arguments and you will be promoted from the domain controller, user/password and if you want to save the cookies and log the output to a file.

Alternatively  you can specify the above on the commandline

GetDirSyncChanges –server:<IP of fqdn of domain controller> -user:<username> -password:<password>  -savecookies  -savelogfile
The user can be domain\user or just user or even a UPN

## Permissions required

The account used will determine what changes will be shown, a standard “user” account will show what changes are happening in AD that this standard user account has permissions to read. 
If you want to see all changes in all partitions hosted on the specified domain controller then use a domain admin level account or alternatively give the account the Replicating Directory Changes permission on the required partitions
https://learn.microsoft.com/en-us/troubleshoot/windows-server/windows-security/grant-replicating-directory-changes-permission-adma-service

Replicating Directory Changes All permission is NOT required. This permission allows password hashes to be retrieved, however the LDAP control that is being used (DirSync) in the code is not capable of this, only DRS_GetNCChanges can do this.
GetDirSyncChanges will detect when a password has been changed, but will not show the hash.

## How it works

An initial sync is first performed, this is required so we have a cookie which can then be used to see what changes have happened. This initial sync queries the AD for a non existent objectclass. A cookie will be generated and is saved in memory. If you selected to save cookies, the cookie will also be written to disk. 

The code will then regularly poll the domain controller asking to see what changes have happened since the last time it polled. These results are returned by the DirSync control showing what object were changed, which attributes were changed and their new values.

However additional data is required (source, local USN, what domain controller the change happed on for replicated changes ) so the metadata is also retrieved for changed objects.  For this I retrieve the data using a not well known option of getting the data in binary and parsing the binary blob returned. This is far more efficient than the standard XML data using less bandwidth and processing on the domain controller.

The cookie is then updated in memory and optionally on the disk, if the option was selected. The process then repeats, looking for new changes since the last time it polled the AD.
If you don’t save the cookies, then an initial sync will be performed each time the code is run. 

