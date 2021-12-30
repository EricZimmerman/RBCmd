# RBCmd

## Command Line Interface

     RBCmd version 0.5.0.0
    
     Author: Eric Zimmerman (saericzimmerman@gmail.com)
     https://github.com/EricZimmerman/RBCmd
     
             d               Directory to recursively process. Either this or -f is required
             f               File to process. Either this or -d is required
             q               Only show the filename being processed vs all output. Useful to speed up exporting to json and/or csv
     
             csv             Directory to save CSV formatted results to. Be sure to include the full path in double quotes
             csvf            File name to save CSV formatted results to. When present, overrides default name
     
             dt              The custom date/time format to use when displaying time stamps. See https://goo.gl/CNVq0k for options. Default is: yyyy-MM-dd HH:mm:ss
     
             debug           Show debug information during processing
             trace           Show trace information during processing
     
     
     Examples: RBCmd.exe -f "C:\Temp\INFO2"
               RBCmd.exe -f "C:\Temp\$I3VPA17" --csv "D:\csvOutput"
               RBCmd.exe -d "C:\Temp" --csv "c:\temp"

               Short options (single letter) are prefixed with a single dash. Long commands are prefixed with two dashes

## Documentation

Windows Recycle Bin artifact parser.

# Download Eric Zimmerman's Tools

All of Eric Zimmerman's tools can be downloaded [here](https://ericzimmerman.github.io/#!index.md). Use the [Get-ZimmermanTools](https://f001.backblazeb2.com/file/EricZimmermanTools/Get-ZimmermanTools.zip) PowerShell script to automate the download and updating of the EZ Tools suite. Additionally, you can automate each of these tools using [KAPE](https://www.kroll.com/en/services/cyber-risk/incident-response-litigation-support/kroll-artifact-parser-extractor-kape)!

# Special Thanks

Open Source Development funding and support provided by the following contributors: 
- [SANS Institute](http://sans.org/) and [SANS DFIR](http://dfir.sans.org/).
- [Tines](https://www.tines.com/?utm_source=oss&utm_medium=sponsorship&utm_campaign=ericzimmerman)
