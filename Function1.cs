using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace SASServiceFunction
{
    public static class Function1
    {
        [FunctionName("Function1")]
        public static void Run([BlobTrigger("docimages/{name}", Connection = "StorageConn")]Stream myBlob, string name, ILogger log)
        {
            // 1. Generate SAS token
            var storageAccount = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("StorageConn"));
            var blobClient = storageAccount.CreateCloudBlobClient();
            var container = blobClient.GetContainerReference("docimages");
            var permissions = SharedAccessBlobPermissions.Read; // default to read permissions
            var sasToken = GetBlobSasToken(container, name, permissions);

            log.LogInformation($"Generated SAS for {name}");

            // 2. Write SAS to SQL
            using (SqlConnection openCon = new SqlConnection(Environment.GetEnvironmentVariable("SQLconnstring")))
            {
                string saveSAS = $"INSERT into TestSAS (BlobName,BlobSAS) VALUES (@BlobName,@BlobSAS)";

                using (SqlCommand sqlcmd = new SqlCommand(saveSAS))
                {
                    sqlcmd.Connection = openCon;
                    sqlcmd.Parameters.Add("@BlobName", SqlDbType.NVarChar).Value =
                        $"{Environment.GetEnvironmentVariable("StorageURL")}/docimages/{name}";
                    sqlcmd.Parameters.Add("@BlobSAS", SqlDbType.NVarChar).Value = sasToken;

                    openCon.Open();

                    sqlcmd.ExecuteNonQuery();
                }
            }

            log.LogInformation($"SQL updated for {name}");
        }

        public static string GetBlobSasToken(CloudBlobContainer container, string blobName, SharedAccessBlobPermissions permissions, string policyName = null)
        {
            string sasBlobToken;

            // Get a reference to a blob within the container.
            // Note that the blob may not exist yet, but a SAS can still be created for it.
            CloudBlockBlob blob = container.GetBlockBlobReference(blobName);

            if (policyName == null)
            {
                var adHocSas = CreateAdHocSasPolicy(permissions);

                // Generate the shared access signature on the blob, setting the constraints directly on the signature.
                sasBlobToken = blob.GetSharedAccessSignature(adHocSas);
            }
            else
            {
                // Generate the shared access signature on the blob. In this case, all of the constraints for the
                // shared access signature are specified on the container's stored access policy.
                sasBlobToken = blob.GetSharedAccessSignature(null, policyName);
            }

            return sasBlobToken;
        }

        private static SharedAccessBlobPolicy CreateAdHocSasPolicy(SharedAccessBlobPermissions permissions)
        {
            // Create a new access policy and define its constraints.
            // Note that the SharedAccessBlobPolicy class is used both to define the parameters of an ad-hoc SAS, and 
            // to construct a shared access policy that is saved to the container's shared access policies. 

            return new SharedAccessBlobPolicy()
            {
                SharedAccessStartTime = DateTime.UtcNow,
                SharedAccessExpiryTime = DateTime.UtcNow.AddYears(100),
                Permissions = permissions
            };
        }
    }
}
