using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Options;
using OrchardCore.Rules.Models;
using OrchardCore.Rules.ViewModels;

namespace OrchardCore.Rules.ViewComponents
{
    public class SelectStringOperationViewComponent : ViewComponent
    {
        private readonly ConditionOperatorOptions _options;

        public SelectStringOperationViewComponent(IOptions<ConditionOperatorOptions> options)
        {
            _options = options.Value;
        }

        public IViewComponentResult Invoke(string selectedOperation, string htmlName)
        {
            var stringOperators = _options.Operators.Where(x => typeof(StringOperator).IsAssignableFrom(x.Operator));

            var items = stringOperators
                .Select(x => 
                    new SelectListItem(
                        x.DisplayText, 
                        x.Operator.Name, 
                        String.Equals(x.Factory.Name, selectedOperation, StringComparison.OrdinalIgnoreCase))
                ).ToList();

            var model = new SelectStringOperationViewModel
            {
                HtmlName = htmlName,
                Items = items
            };

            return View(model);
        }
    }
}
