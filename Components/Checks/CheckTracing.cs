﻿using System.Web;
using System.Web.UI;

namespace DNN.Modules.SecurityAnalyzer.Components.Checks
{
    public class CheckTracing : IAuditCheck
    {
        public string Id => "CheckTracing";

        public bool LazyLoad => false;

        public CheckResult Execute()
        {
            var result = new CheckResult(SeverityEnum.Unverified, Id);
            var page = HttpContext.Current.Handler as Page;

            if (page != null)
            {
                result.Severity = page.TraceEnabled ? SeverityEnum.Failure : SeverityEnum.Pass;
            }
            return result;
        }
    }
}