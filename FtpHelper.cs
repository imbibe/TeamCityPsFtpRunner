#region Copyright
// 
// Imbibe Technologies Pvt Ltd® - http://imbibe.in
// Copyright (c) 2014
// by Imbibe Technologies Pvt Ltd
// 
// This software and associated files including documentation (the "Software") is goverened by Microsoft Public License (MS-PL),
// a copy of which is included with the Software as a text file, License.txt.
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace FtpHelper
{
	public class FtpHelper
	{
		#region Private Members
		private string baseServerUrl;
		private string username;
		private string password;
		private bool usePassiveMode;

		private List<string> ensuredDirectories = new List<string>();
		#endregion

		#region Constructors
		public FtpHelper (string baseServerUrl, string username, string password)
			: this(baseServerUrl, username, password, true)
		{
		}

		public FtpHelper (string baseServerUrl, string username, string password, bool usePassiveMode)
		{
			if (!baseServerUrl.EndsWith("/"))
			{
				baseServerUrl += "/";
			}

			this.baseServerUrl = baseServerUrl;
			this.username = username;
			this.password = password;
			this.usePassiveMode = usePassiveMode;
		}
		#endregion

		#region Public Methods
		public long? GetFileSize (string relativeFileUrl, bool ignoreErrors = false)
		{
			try
			{
				var ftpRequest = this.GetFtpRequest(relativeFileUrl);

				ftpRequest.Method = WebRequestMethods.Ftp.GetFileSize;
				using (var ftpResponse = (FtpWebResponse) ftpRequest.GetResponse())
				{
					this.ValidateFtpResponse(ftpResponse, ignoreErrors);

					return (ftpResponse.ContentLength);
				}
			}
			catch (Exception)
			{
				if (!ignoreErrors)
				{
					throw;
				}

				return (null);
			}
		}

		public void DeleteFile (string relativeFileUrl, bool ignoreErrors = false)
		{
			try
			{
				var ftpRequest = this.GetFtpRequest(relativeFileUrl);

				ftpRequest.Method = WebRequestMethods.Ftp.DeleteFile;
				using (var ftpResponse = (FtpWebResponse) ftpRequest.GetResponse())
				{
					this.ValidateFtpResponse(ftpResponse, ignoreErrors);
				}
			}
			catch (Exception)
			{
				if (!ignoreErrors)
				{
					throw;
				}
			}
		}

		/// <summary>
		/// A utility function kept for the sake of completeness, the function is not really meant to download large files (MemoryStream is not the ideal stream to handle large files).
		/// This whole class is meant as a helper for FTP functionality in TeamCity builds which would usually involve pushing files/direcotries to deployment server, not downloading content from the server.
		/// </summary>
		public Stream DownloadFile (string relativeFileUrl, bool ignoreErrors = false)
		{
			try
			{
				var ftpRequest = this.GetFtpRequest(relativeFileUrl);

				ftpRequest.Method = WebRequestMethods.Ftp.DownloadFile;
				using (var ftpResponse = (FtpWebResponse) ftpRequest.GetResponse())
				{
					this.ValidateFtpResponse(ftpResponse, ignoreErrors);

					using (var responseStream = ftpResponse.GetResponseStream())
					{
						var memoryStream = new MemoryStream();
						responseStream.CopyTo(memoryStream);

						memoryStream.Position = 0;

						return (memoryStream);
					}
				}
			}
			catch (Exception)
			{
				if (!ignoreErrors)
				{
					throw;
				}

				return (null);
			}
		}

		public void DownloadFile (string relativeFileUrl, string targetPath, bool ignoreErrors = false)
		{
			try
			{
				var stream = this.DownloadFile(relativeFileUrl,
					ignoreErrors: ignoreErrors);
				using (stream)
				{
					using (var fs = File.OpenWrite(targetPath))
					{
						stream.CopyTo(fs);
					}
				}
			}
			catch (Exception)
			{
				if (!ignoreErrors)
				{
					throw;
				}
			}
		}

		public void UploadFile (string relativeFileUrl, Stream stream, bool overwrite = true, bool ensureDirectoryTree = true, bool ignoreErrors = false)
		{
			try
			{
				if (ensureDirectoryTree)
				{
					var parts = this.SplitPath(relativeFileUrl);

					var currentPath = "";

					//Last part would be the file name.
					for (var i = 0; i < parts.Length - 1; i++)
					{
						var part = parts[i];

						currentPath += "/" + part;
						this.EnsureDirectoryExists(currentPath, ignoreErrors);
					}
				}

				if (!overwrite)
				{
					var size = this.GetFileSize(relativeFileUrl,
						ignoreErrors: true);
					if (size.HasValue)
					{
						throw new IOException("The file already exists.");
					}
				}

				var ftpRequest = this.GetFtpRequest(relativeFileUrl);

				ftpRequest.Method = WebRequestMethods.Ftp.UploadFile;
				using (var requestStream = ftpRequest.GetRequestStream())
				{
					stream.CopyTo(requestStream);

					requestStream.Close();
				}

				using (var ftpResponse = (FtpWebResponse) ftpRequest.GetResponse())
				{
					this.ValidateFtpResponse(ftpResponse, ignoreErrors);
				}
			}
			catch (Exception)
			{
				if (!ignoreErrors)
				{
					throw;
				}
			}
		}

		public void UploadFile (string relativeFileUrl, string sourceFilePath, bool overwrite = true, bool ignoreErrors = false)
		{
			try
			{
				using (var fs = File.OpenRead(sourceFilePath))
				{
					this.UploadFile(relativeFileUrl, fs,
						overwrite: overwrite,
						ignoreErrors: ignoreErrors);
				}
			}
			catch (Exception)
			{
				if (!ignoreErrors)
				{
					throw;
				}
			}
		}

		public void MakeDirectory (string relativeDirUrl, bool ignoreErrors = false)
		{
			try
			{
				var ftpRequest = this.GetFtpRequest(relativeDirUrl);

				ftpRequest.Method = WebRequestMethods.Ftp.MakeDirectory;
				using (var ftpResponse = (FtpWebResponse) ftpRequest.GetResponse())
				{
					this.ValidateFtpResponse(ftpResponse, ignoreErrors);
				}
			}
			catch (Exception)
			{
				if (!ignoreErrors)
				{
					throw;
				}
			}
		}

		public void RemoveDirectory (string relativeDirUrl, bool recursive = false, bool ignoreErrors = false)
		{
			try
			{
				if (!recursive)
				{
					var ftpRequest = this.GetFtpRequest(relativeDirUrl);
					ftpRequest.Method = WebRequestMethods.Ftp.RemoveDirectory;
					using (var ftpResponse = (FtpWebResponse) ftpRequest.GetResponse())
					{
						this.ValidateFtpResponse(ftpResponse, ignoreErrors);
					}

					return;
				}

				var contents = this.ListDirectoryDetails(relativeDirUrl,
					ignoreErrors: ignoreErrors);
				foreach (var content in contents)
				{
					var childPath = this.CombinePaths(relativeDirUrl, content.name);
					if (content.isFile)
					{
						this.DeleteFile(childPath,
							ignoreErrors: ignoreErrors);
					}
					else
					{
						this.RemoveDirectory(childPath,
							recursive: recursive,
							ignoreErrors: ignoreErrors);
					}
				}

				this.RemoveDirectory(relativeDirUrl,
					recursive: false,
					ignoreErrors: ignoreErrors);
			}
			catch (Exception)
			{
				if (!ignoreErrors)
				{
					throw;
				}
			}
		}

		public string[] ListDirectory (string relativeDirUrl, bool ignoreErrors = false)
		{
			try
			{
				var ftpRequest = this.GetFtpRequest(relativeDirUrl);

				ftpRequest.Method = WebRequestMethods.Ftp.ListDirectory;
				using (var ftpResponse = (FtpWebResponse) ftpRequest.GetResponse())
				{
					this.ValidateFtpResponse(ftpResponse, ignoreErrors);

					using (var responseStream = ftpResponse.GetResponseStream())
					{
						using (var streamReader = new StreamReader(responseStream))
						{
							var s = streamReader.ReadToEnd();
							return (s.Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries));
						}
					}
				}
			}
			catch (Exception)
			{
				if (!ignoreErrors)
				{
					throw;
				}

				return (null);
			}
		}

		public List<FtpObjectInfo> ListDirectoryDetails (string relativeDirUrl, bool ignoreErrors = false)
		{
			try
			{
				var ftpRequest = this.GetFtpRequest(relativeDirUrl);

				ftpRequest.Method = WebRequestMethods.Ftp.ListDirectoryDetails;
				using (var ftpResponse = (FtpWebResponse) ftpRequest.GetResponse())
				{
					this.ValidateFtpResponse(ftpResponse, ignoreErrors);

					using (var responseStream = ftpResponse.GetResponseStream())
					{
						using (var streamReader = new StreamReader(responseStream))
						{
							var s = streamReader.ReadToEnd();
							var lines = s.Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);

							var l = new List<FtpObjectInfo>();
							foreach (var line in lines)
							{
								l.Add(this.ParseListDirectoryDetailsResponseLine(line));
							}

							return (l);
						}
					}
				}
			}
			catch (Exception)
			{
				if (!ignoreErrors)
				{
					throw;
				}

				return (null);
			}
		}

		public void EnsureDirectoriesExist (string[] relativePaths, bool ignoreErrors = false)
		{
			foreach (var relativePath in relativePaths)
			{
				this.EnsureDirectoryExists(relativePath,
					ignoreErrors: ignoreErrors);
			}
		}

		public void EnsureDirectoryExists (string relativePath, bool ignoreErrors = false)
		{
			try
			{
				if (this.ensuredDirectories.Contains(relativePath))
				{
					return;
				}

				//Do not ignore error for ListDirectory call as only exception would tell us Directory does not exist.
				this.ListDirectory(relativePath,
					ignoreErrors: false);

				this.ensuredDirectories.Add(relativePath);
				return;
			}
			catch
			{
			}

			this.MakeDirectory(relativePath,
				ignoreErrors: ignoreErrors);
			this.ensuredDirectories.Add(relativePath);
		}
		#endregion

		#region Protected Methods
		protected virtual FtpWebRequest GetFtpRequest (string relativeUrl)
		{
			var url = this.ResolveRelativeUrl(relativeUrl);

			var ftpRequest = (FtpWebRequest) WebRequest.Create(url);
			
			if(!string.IsNullOrEmpty(this.username)) {
				ftpRequest.Credentials = new NetworkCredential(this.username, this.password);
			}

			ftpRequest.UsePassive = this.usePassiveMode;

			return (ftpRequest);
		}

		protected virtual string ResolveRelativeUrl (string relativeUrl)
		{
			if (relativeUrl.Length == 0)
			{
				return (this.baseServerUrl);
			}

			if (relativeUrl[0] == '/')
			{
				relativeUrl = relativeUrl.Substring(1);
			}

			return (this.baseServerUrl + relativeUrl);
		}

		protected virtual string CombinePaths (string path1, string path2)
		{
			path1 = path1.Trim('/');
			path2 = path2.Trim('/');

			return (path1 + "/" + path2);
		}

		protected virtual void ValidateFtpResponse (FtpWebResponse ftpResponse, bool ignoreErrors)
		{
			if (ignoreErrors)
			{
				return;
			}

			switch (ftpResponse.StatusCode)
			{
				case FtpStatusCode.CommandOK:
				case FtpStatusCode.FileActionOK:
				case FtpStatusCode.FileStatus:
				case FtpStatusCode.DirectoryStatus:
				case FtpStatusCode.OpeningData:
				case FtpStatusCode.ClosingData:
				case FtpStatusCode.PathnameCreated:
					break;

				default:
					throw new Exception(string.Format("Operatin failed:- {0}", ftpResponse.StatusDescription));
			}
		}

		protected virtual string[] SplitPath (string path)
		{
			return (path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries));
		}

		protected virtual FtpObjectInfo ParseListDirectoryDetailsResponseLine (string line)
		{
			var info = new FtpObjectInfo();

			string part;
			int index;

			part = line.Substring(0, 1); line = line.Substring(1).Trim();
			info.isFile = part != "d";

			part = line.Substring(0, 9); line = line.Substring(9).Trim();
			info.permissions = part;

			part = line.Substring(0, 1); line = line.Substring(1).Trim();
			//info.isFile = part != "d";

			index = line.IndexOf(' ');
			part = line.Substring(0, index); line = line.Substring(index + 1).Trim();
			info.username = part;

			index = line.IndexOf(' ');
			part = line.Substring(0, index); line = line.Substring(index + 1).Trim();
			info.group = part;

			index = line.IndexOf(' ');
			part = line.Substring(0, index); line = line.Substring(index + 1).Trim();
			info.size = long.Parse(part);

			index = line.IndexOf(' '); while (line[index + 1] == ' ') index++;
			index = line.IndexOf(' ', index + 1); while (line[index + 1] == ' ') index++;
			index = line.IndexOf(' ', index + 1);
			part = line.Substring(0, index); line = line.Substring(index + 1).Trim();
			while ((index = part.IndexOf("  ")) != -1)
			{
				part = part.Replace("  ", " ");
			}
			if (part.IndexOf(':') != -1)
			{
				info.lastModified = DateTime.SpecifyKind(DateTime.ParseExact(part + " " + DateTime.UtcNow.Year.ToString(), "MMM d HH:mm yyyy", System.Globalization.DateTimeFormatInfo.InvariantInfo), DateTimeKind.Utc);
			}
			else
			{
				info.lastModified = DateTime.SpecifyKind(DateTime.ParseExact(part, "MMM d yyyy", System.Globalization.DateTimeFormatInfo.InvariantInfo), DateTimeKind.Utc);
			}

			info.name = line;

			return (info);
		}
		#endregion

		#region Inner Types
		public class FtpObjectInfo
		{
			public bool isFile { get; set; }
			public string permissions { get; set; }
			public string username { get; set; }
			public string group { get; set; }
			public long size { get; set; }
			public DateTime lastModified { get; set; }
			public string name { get; set; }
		}
		#endregion
	}
}
