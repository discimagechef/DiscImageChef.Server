﻿// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : UploadReportController.cs
// Author(s)      : Natalia Portillo <claunia@claunia.com>
//
// Component      : DiscImageChef Server.
//
// --[ Description ] ----------------------------------------------------------
//
//     Handles report uploads.
//
// --[ License ] --------------------------------------------------------------
//
//     This library is free software; you can redistribute it and/or modify
//     it under the terms of the GNU Lesser General Public License as
//     published by the Free Software Foundation; either version 2.1 of the
//     License, or (at your option) any later version.
//
//     This library is distributed in the hope that it will be useful, but
//     WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//     Lesser General Public License for more details.
//
//     You should have received a copy of the GNU Lesser General Public
//     License along with this library; if not, see <http://www.gnu.org/licenses/>.
//
// ----------------------------------------------------------------------------
// Copyright © 2011-2018 Natalia Portillo
// ****************************************************************************/

using System;
using System.IO;
using System.Net.Http;
using System.Web;
using System.Web.Http;
using System.Xml.Serialization;
using DiscImageChef.Metadata;

namespace DiscImageChef.Server.Controllers
{
    public class UploadReportController : ApiController
    {
        [Route("api/uploadreport")]
        [HttpPost]
        public HttpResponseMessage UploadReport()
        {
            HttpResponseMessage response = new HttpResponseMessage();
            response.StatusCode = System.Net.HttpStatusCode.OK;

            try
            {
                DeviceReport newReport = new DeviceReport();
                HttpRequest request = HttpContext.Current.Request;

                if(request.InputStream == null)
                {
                    response.Content = new StringContent("notstats", System.Text.Encoding.UTF8, "text/plain");
                    return response;
                }

                XmlSerializer xs = new XmlSerializer(newReport.GetType());
                newReport = (DeviceReport)xs.Deserialize(request.InputStream);

                if(newReport == null)
                {
                    response.Content = new StringContent("notstats", System.Text.Encoding.UTF8, "text/plain");
                    return response;
                }

                Random rng = new Random();
                string filename = string.Format("NewReport_{0:yyyyMMddHHmmssfff}_{1}.xml", DateTime.UtcNow, rng.Next());
                while(File.Exists(Path.Combine(System.Web.Hosting.HostingEnvironment.MapPath("~"), "Upload", filename)))
                {
                    filename = string.Format("NewReport_{0:yyyyMMddHHmmssfff}_{1}.xml", DateTime.UtcNow, rng.Next());
                }

                FileStream newFile = new FileStream(Path.Combine(System.Web.Hosting.HostingEnvironment.MapPath("~"), "Upload", filename), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                xs.Serialize(newFile, newReport);
                newFile.Close();

                response.Content = new StringContent("ok", System.Text.Encoding.UTF8, "text/plain");
                return response;
            }
            catch
            {
#if DEBUG
                throw;
#else
                response.Content = new StringContent("error", System.Text.Encoding.UTF8, "text/plain");
                return response;
#endif
            }
        }
    }
}
