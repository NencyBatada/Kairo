@{
    development = @{
        Project             = 'kairo2'
        Region              = 'asia-south1'
        SqlTier             = 'db-custom-1-3840'
        SqlEdition          = 'SQLSERVER_2022_EXPRESS'
        SqlHighAvailability = $false
        MinInstances        = @{ web = 0; api = 0 }
        MailSender          = 'evolixgcpadmin@gmail.com'
    }
    staging     = @{ Project = '' }
    production  = @{ Project = '' }
}
