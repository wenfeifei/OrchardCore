using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using OrchardCore.Rules.Models;
using OrchardCore.Rules.ViewModels;
using OrchardCore.DisplayManagement.Handlers;
using OrchardCore.DisplayManagement.ModelBinding;
using OrchardCore.DisplayManagement.Views;

namespace OrchardCore.Rules.Drivers
{
    public class JavascriptConditionDisplayDriver : DisplayDriver<Condition, JavascriptCondition>
    {
        private readonly ConditionOperatorOptions _options;

        public JavascriptConditionDisplayDriver(IOptions<ConditionOperatorOptions> options)
        {
            _options = options.Value;
        }
        
        public override IDisplayResult Display(JavascriptCondition condition)
        {
            return
                Combine(
                    View("JavascriptCondition_Fields_Summary", condition).Location("Summary", "Content"),
                    View("JavascriptCondition_Fields_Thumbnail", condition).Location("Thumbnail", "Content")
                );
        }

        public override IDisplayResult Edit(JavascriptCondition condition)
        {
            return Initialize<JavascriptConditionViewModel>("JavascriptCondition_Fields_Edit", m =>
            {
                m.Script = condition.Script;
                m.Condition = condition;
            }).Location("Content");
        }

        public override async Task<IDisplayResult> UpdateAsync(JavascriptCondition condition, IUpdateModel updater)
        {
            var model = new JavascriptConditionViewModel();
            if (await updater.TryUpdateModelAsync(model, Prefix))
            {
                // TODO is empty.
                condition.Script = model.Script;
            }

            return Edit(condition);
        }
    }
}
