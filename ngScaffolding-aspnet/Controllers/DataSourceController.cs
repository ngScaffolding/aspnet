﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ngScaffolding.database;
using ngScaffolding.Data;
using ngScaffolding.Helpers;
using ngScaffolding.Infrastructure;
using ngScaffolding.Models;
using ngScaffolding.Models.DataSourceModels;
using ngScaffolding.Services;
using Newtonsoft.Json;
using ngScaffolding.database.Models;
using System.Dynamic;
using Newtonsoft.Json.Converters;
using System.Net;
using System.IO;

namespace ngScaffolding.Controllers
{
    [Route("api/DataSource")]
    public class DataSourceController : ngScaffoldingController
    {
        private readonly IConnectionStringsService _connectionStringsService;
        private readonly IAPILocationsService _apiLocationsService;
        private readonly IRepository<DataSource> _dataSourceRepository;

        public IRepository<MenuItem> _menuItemRepository { get; }

        public class DataSourceRequest
        {
            public int Id { get; set; }
            public string Seed { get; set; }
            public string FilterValues { get; set; }
            public string RowData { get; set; }
            public string InputData { get; set; }
            public int? PageNumber { get; set; }
            public int? PageSize { get; set; }
        }

        public class DataResults
        {
            public int rowCount { get; set; }
            public string jsonData { get; set; }
            public ICollection<ActionResult> results { get; set; }
        }

        public DataSourceController(IConnectionStringsService connectionStringsService,
            IAPILocationsService apiLocationsService,
            IRepository<MenuItem> menuItemRepository,
            IRepository<DataSource> dataSourceRepository)
        {
            _connectionStringsService = connectionStringsService;
            _apiLocationsService = apiLocationsService;
            _menuItemRepository = menuItemRepository;
            _dataSourceRepository = dataSourceRepository;
        }

        // POST: api/DataSource
        [HttpPost]
        //[ServiceFilter(typeof(AuditAttribute))]
        public async Task<IActionResult> Post([FromBody] DataSourceRequest dataSourceRequest)
        {
            if (dataSourceRequest != null)
            {
                var dataSource = _dataSourceRepository.Get(dataSourceRequest.Id);

                var baseDataSource = JsonConvert.DeserializeObject<BaseDataSource>(dataSource.JsonContent);

                if (dataSource != null)
                {
                    dynamic filterValues = null;

                    // Work out Filter Values
                    if (!string.IsNullOrEmpty(dataSourceRequest.FilterValues))
                    {
                        var converter = new ExpandoObjectConverter();
                        filterValues = JsonConvert.DeserializeObject<ExpandoObject>(dataSourceRequest.FilterValues, converter);
                    }

                    switch (baseDataSource.type)
                    {
                        case BaseDataSource.TypesSql:
                            {
                                var sqlHelper = new SqlDataHelper(_connectionStringsService);
                                var sqlDatasource = JsonConvert.DeserializeObject<SqlDataSource>(dataSource.JsonContent);
                                if (sqlDatasource != null)
                                {
                                    var sqlResults = await sqlHelper.RunCommand(sqlDatasource, filterValues);

                                    var retVal = new DataResults()
                                    {
                                        rowCount = sqlResults.RowCount,
                                        jsonData = JsonConvert.SerializeObject(sqlResults.Results, Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Include }),
                                        results = sqlResults.ActionResults
                                    };

                                    return Ok(retVal);
                                }
                                break;
                            }
                        case BaseDataSource.TypesRestApi:
                            {
                                var apiDataSource = JsonConvert.DeserializeObject<RestApiDataSource>(dataSource.JsonContent);

                                // Get Server details from app.config
                                var apiSettings = _apiLocationsService.Get(apiDataSource.serverName);

                                var url = apiDataSource.url;

                                // Variable replacement
                                if (filterValues != null)
                                {
                                    IDictionary<string, object> propertyValues = (IDictionary<string, object>)filterValues;
                                    foreach (var property in propertyValues)
                                    {
                                        var searchKey = string.Format("@@{0}", property.Key);
                                        if (url.Contains(searchKey))
                                        {
                                            url = url.Replace(searchKey, property.Value.ToString());
                                        }
                                    }
                                }

                                var request = (HttpWebRequest)WebRequest.Create(apiSettings.serverUrl + url);

                                request.Method = "GET";
                                request.UserAgent = "Mozilla/5.0 (Windows NT 6.1; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.36";
                                request.AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip;

                                var response = (HttpWebResponse)request.GetResponse();

                                string content = string.Empty;
                                using (var stream = response.GetResponseStream())
                                {
                                    using (var sr = new StreamReader(stream))
                                    {
                                        content = sr.ReadToEnd();
                                    }
                                }

                                var retVal = new DataResults()
                                {
                                    rowCount = 0,
                                    jsonData = content,
                                    results = new List<ActionResult> { new ActionResult { success = true } }
                                };

                                return Ok(retVal);
                            }
                    }


                }
            }
            return NotFound();
        }
    }
}
