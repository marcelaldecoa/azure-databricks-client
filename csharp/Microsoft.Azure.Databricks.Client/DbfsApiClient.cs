﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Microsoft.Azure.Databricks.Client
{
    public sealed class DbfsApiClient : ApiClient, IDbfsApi
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DbfsApiClient"/> class.
        /// </summary>
        /// <param name="httpClient">The HTTP client.</param>
        public DbfsApiClient(HttpClient httpClient) : base(httpClient)
        {
        }

        public async Task<long> Create(string path, bool overwrite)
        {
            var request = new { path, overwrite };
            var response = await HttpPost<dynamic, FileHandle>(this.HttpClient, "dbfs/create", request).ConfigureAwait(false);
            return response.Handle;
        }

        public async Task AddBlock(long fileHandle, byte[] data)
        {
            var request = new { handle = fileHandle, data };
            await HttpPost(this.HttpClient, "dbfs/add-block", request).ConfigureAwait(false);
        }

        public async Task Close(long fileHandle)
        {
            var handle = new FileHandle(fileHandle);
            await HttpPost(this.HttpClient, "dbfs/close", handle).ConfigureAwait(false);
        }

        public async Task Upload(string path, bool overwrite, Stream stream)
        {
            const int mb = 1024 * 1024;
            var handle = await this.Create(path, overwrite).ConfigureAwait(false);

            var originalPosition = 0L;

            if (stream.CanSeek)
            {
                originalPosition = stream.Position;
                stream.Position = 0;
            }

            var buffer = new byte[mb];
            try
            {
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, 0, mb)) > 0)
                {
                    var contents = new byte[bytesRead];
                    Array.Copy(buffer, contents, bytesRead);
                    await this.AddBlock(handle, contents).ConfigureAwait(false);
                }

                await this.Close(handle).ConfigureAwait(false);
            }
            finally
            {
                if (stream.CanSeek)
                {
                    stream.Position = originalPosition;
                }
            }
        }

        public async Task Delete(string path, bool recursive)
        {
            var request = new { path, recursive };
            await HttpPost(this.HttpClient, "dbfs/delete", request).ConfigureAwait(false);
        }

        public async Task<FileInfo> GetStatus(string path)
        {
            var url = $"dbfs/get-status?path={path}";
            var result = await HttpGet<FileInfo>(this.HttpClient, url).ConfigureAwait(false);
            return result;
        }

        public async Task<IEnumerable<FileInfo>> List(string path)
        {
            var url = $"dbfs/list?path={path}";
            var result = await HttpGet<dynamic>(this.HttpClient, url).ConfigureAwait(false);
            return PropertyExists(result, "files")
                ? result.files.ToObject<IEnumerable<FileInfo>>()
                : Enumerable.Empty<FileInfo>();
        }

        public async Task Mkdirs(string path)
        {
            var request = new { path };
            await HttpPost(this.HttpClient, "dbfs/mkdirs", request).ConfigureAwait(false);
        }

        public async Task Move(string sourcePath, string destinationPath)
        {
            var request = new { source_path = sourcePath, destination_path = destinationPath };
            await HttpPost(this.HttpClient, "dbfs/move", request).ConfigureAwait(false);
        }

        public async Task Put(string path, byte[] contents, bool overwrite)
        {
            var form = new MultipartFormDataContent
                {
                    {new StringContent(path), "path"},
                    {new StringContent(overwrite.ToString().ToLowerInvariant()), "overwrite"},
                    {new ByteArrayContent(contents), "contents"}
                };

            var response = await this.HttpClient.PostAsync("dbfs/put", form).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw CreateApiException(response);
            }
        }

        public async Task<FileReadBlock> Read(string path, long offset, long length)
        {
            var url = $"dbfs/read?path={path}&offset={offset}&length={length}";
            var result = await HttpGet<FileReadBlock>(this.HttpClient, url).ConfigureAwait(false);
            return result;
        }

        public async Task Download(string path, Stream stream)
        {
            const int mb = 1024 * 1024;
            var totalBytesRead = 0L;
            var block = await Read(path, totalBytesRead, mb);

            while (block.BytesRead > 0)
            {
                totalBytesRead += block.BytesRead;
                await stream.WriteAsync(block.Data, 0, block.Data.Length);
                block = await Read(path, totalBytesRead, mb);
            }
        }
    }
}