using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using OrchardCore.DisplayManagement;
using OrchardCore.DisplayManagement.ModelBinding;
using OrchardCore.DisplayManagement.Notify;
using OrchardCore.Navigation;
using OrchardCore.Routing;
using OrchardCore.Security.Services;
using OrchardCore.Settings;
using OrchardCore.Users.Indexes;
using OrchardCore.Users.Models;
using OrchardCore.Users.Services;
using OrchardCore.Users.ViewModels;
using YesSql;
using YesSql.Services;
using YesSql.Sql;

namespace OrchardCore.Users.Controllers
{
    public class AdminController : Controller
    {
        private readonly UserManager<IUser> _userManager;
        private readonly IDisplayManager<UserIndexOptions> _userOptionsDisplayManager;
        private readonly SignInManager<IUser> _signInManager;
        private readonly ISession _session;
        private readonly IAuthorizationService _authorizationService;
        private readonly ISiteService _siteService;
        private readonly IDisplayManager<User> _userDisplayManager;
        private readonly INotifier _notifier;
        private readonly IUserService _userService;
        private readonly IRoleService _roleService;
        private readonly IUsersAdminListQueryService _usersAdminListQueryService;
        private readonly IUpdateModelAccessor _updateModelAccessor;
        private readonly IShapeFactory _shapeFactory;

        private readonly dynamic New;
        private readonly IHtmlLocalizer H;
        private readonly IStringLocalizer S;

        public AdminController(
            IDisplayManager<User> userDisplayManager,
            IDisplayManager<UserIndexOptions> userOptionsDisplayManager,
            SignInManager<IUser> signInManager,
            IAuthorizationService authorizationService,
            ISession session,
            UserManager<IUser> userManager,
            IUserService userService,
            IRoleService roleService,
            IUsersAdminListQueryService usersAdminListQueryService,
            INotifier notifier,
            ISiteService siteService,
            IShapeFactory shapeFactory,
            IHtmlLocalizer<AdminController> htmlLocalizer,
            IStringLocalizer<AdminController> stringLocalizer,
            IUpdateModelAccessor updateModelAccessor)
        {
            _userDisplayManager = userDisplayManager;
            _userOptionsDisplayManager = userOptionsDisplayManager;
            _signInManager = signInManager;
            _authorizationService = authorizationService;
            _session = session;
            _userManager = userManager;
            _notifier = notifier;
            _siteService = siteService;
            _userService = userService;
            _roleService = roleService;
            _usersAdminListQueryService = usersAdminListQueryService;
            _updateModelAccessor = updateModelAccessor;
            _shapeFactory = shapeFactory;

            New = shapeFactory;
            H = htmlLocalizer;
            S = stringLocalizer;
        }

        public async Task<ActionResult> Index(UserIndexOptions options, PagerParameters pagerParameters)
        {
            // Check a dummy user account to see if the current user has permission to view users.
            var authUser = new User();

            if (!await _authorizationService.AuthorizeAsync(User, Permissions.ViewUsers, authUser))
            {
                return Forbid();
            }

            var siteSettings = await _siteService.GetSiteSettingsAsync();
            var pager = new Pager(pagerParameters, siteSettings.PageSize);

            var users = await _usersAdminListQueryService.QueryAsync(options, _updateModelAccessor.ModelUpdater);

            var count = await users.CountAsync();

            var results = await users
                .Skip(pager.GetStartIndex())
                .Take(pager.PageSize)
                .ListAsync();

            // Populate route values to maintain previous route data when generating page links
            await _userOptionsDisplayManager.UpdateEditorAsync(options, _updateModelAccessor.ModelUpdater, false);

            var routeData = new RouteData(options.RouteValues);

            var pagerShape = (await New.Pager(pager)).TotalItemCount(count).RouteData(routeData);

            var userEntries = new List<UserEntry>();

            foreach (var user in results)
            {
                userEntries.Add(new UserEntry
                {
                    UserId = user.UserId,
                    Shape = await _userDisplayManager.BuildDisplayAsync(user, updater: _updateModelAccessor.ModelUpdater, displayType: "SummaryAdmin")
                }
                );
            }

            options.UserFilters = new List<SelectListItem>()
            {
                new SelectListItem() { Text = S["All Users"], Value = nameof(UsersFilter.All) },
                new SelectListItem() { Text = S["Enabled Users"], Value = nameof(UsersFilter.Enabled) },
                new SelectListItem() { Text = S["Disabled Users"], Value = nameof(UsersFilter.Disabled) }
                //new SelectListItem() { Text = S["Approved"], Value = nameof(UsersFilter.Approved) },
                //new SelectListItem() { Text = S["Email pending"], Value = nameof(UsersFilter.EmailPending) },
                //new SelectListItem() { Text = S["Pending"], Value = nameof(UsersFilter.Pending) }
            };

            options.UserSorts = new List<SelectListItem>()
            {
                new SelectListItem() { Text = S["Name"], Value = nameof(UsersOrder.Name) },
                new SelectListItem() { Text = S["Email"], Value = nameof(UsersOrder.Email) },
                //new SelectListItem() { Text = S["Created date"], Value = nameof(UsersOrder.CreatedUtc) },
                //new SelectListItem() { Text = S["Last Login date"], Value = nameof(UsersOrder.LastLoginUtc) }
            };

            options.UsersBulkAction = new List<SelectListItem>()
            {
                new SelectListItem() { Text = S["Approve"], Value = nameof(UsersBulkAction.Approve) },
                new SelectListItem() { Text = S["Enable"], Value = nameof(UsersBulkAction.Enable) },
                new SelectListItem() { Text = S["Disable"], Value = nameof(UsersBulkAction.Disable) },
                new SelectListItem() { Text = S["Delete"], Value = nameof(UsersBulkAction.Delete) }
            };

            var allRoles = (await _roleService.GetRoleNamesAsync())
                .Except(new[] { "Anonymous", "Authenticated" }, StringComparer.OrdinalIgnoreCase);

            options.UserRoleFilters = new List<SelectListItem>()
            {
                new SelectListItem() { Text = S["All roles"], Value = String.Empty },
                new SelectListItem() { Text = S["Authenticated (no roles)"], Value = "Authenticated" }
            };

            // TODO Candidate for dynamic localization.
            options.UserRoleFilters.AddRange(allRoles.Select(x => new SelectListItem { Text = x, Value = x }));

            // Populate options pager summary values.
            var startIndex = (pagerShape.Page - 1) * (pagerShape.PageSize) + 1;
            options.StartIndex = startIndex;
            options.EndIndex = startIndex + userEntries.Count - 1;
            options.UsersCount = userEntries.Count;
            options.TotalItemCount = pagerShape.TotalItemCount;

            var header = await _userOptionsDisplayManager.BuildEditorAsync(options, _updateModelAccessor.ModelUpdater, false);

            var shapeViewModel = await _shapeFactory.CreateAsync<UsersIndexViewModel>("UsersAdminList", viewModel =>
            {
                viewModel.Users = userEntries;
                viewModel.Pager = pagerShape;
                viewModel.Options = options;
                viewModel.Header = header;
            });

            return View(shapeViewModel);
        }

        [HttpPost, ActionName("Index")]
        [FormValueRequired("submit.Filter")]
        public async Task<ActionResult> IndexFilterPOST(UsersIndexViewModel model)
        {
            await _userOptionsDisplayManager.UpdateEditorAsync(model.Options, _updateModelAccessor.ModelUpdater, false);

            return RedirectToAction("Index", model.Options.RouteValues);
        }

        [HttpPost, ActionName("Index")]
        [FormValueRequired("submit.BulkAction")]
        public async Task<ActionResult> IndexPOST(UserIndexOptions options, IEnumerable<string> itemIds)
        {
            // Check a dummy user account to see if the current user has permission to manage it.
            var authUser = new User();

            if (!await _authorizationService.AuthorizeAsync(User, Permissions.ManageUsers, authUser))
            {
                return Forbid();
            }

            if (itemIds?.Count() > 0)
            {
                var checkedUsers = await _session.Query<User, UserIndex>().Where(x => x.UserId.IsIn(itemIds)).ListAsync();

                // Bulk actions require the ManageUsers permission on all the checked users.
                // To prevent html injection we authorize each user before performing any operations.
                foreach (var user in checkedUsers)
                {
                    if (!await _authorizationService.AuthorizeAsync(User, Permissions.ManageUsers, user))
                    {
                        return Forbid();
                    }
                }

                switch (options.BulkAction)
                {
                    case UsersBulkAction.None:
                        break;
                    case UsersBulkAction.Approve:
                        foreach (var user in checkedUsers)
                        {
                            if (!await _userManager.IsEmailConfirmedAsync(user))
                            {
                                var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                                await _userManager.ConfirmEmailAsync(user, token);
                                _notifier.Success(H["User {0} successfully approved.", user.UserName]);
                            }
                        }
                        break;
                    case UsersBulkAction.Delete:
                        foreach (var user in checkedUsers)
                        {
                            if (String.Equals(user.UserId, User.FindFirstValue(ClaimTypes.NameIdentifier), StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            await _userManager.DeleteAsync(user);
                            _notifier.Success(H["User {0} successfully deleted.", user.UserName]);
                        }
                        break;
                    case UsersBulkAction.Disable:
                        foreach (var user in checkedUsers)
                        {
                            if (String.Equals(user.UserId, User.FindFirstValue(ClaimTypes.NameIdentifier), StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            user.IsEnabled = false;
                            await _userManager.UpdateAsync(user);
                            _notifier.Success(H["User {0} successfully disabled.", user.UserName]);
                        }
                        break;
                    case UsersBulkAction.Enable:
                        foreach (var user in checkedUsers)
                        {
                            if (String.Equals(user.UserId, User.FindFirstValue(ClaimTypes.NameIdentifier), StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            user.IsEnabled = true;
                            await _userManager.UpdateAsync(user);
                            _notifier.Success(H["User {0} successfully enabled.", user.UserName]);
                        }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return RedirectToAction("Index");
        }
        public async Task<IActionResult> Create()
        {
            var user = new User();

            if (!await _authorizationService.AuthorizeAsync(User, Permissions.ViewUsers, user))
            {
                return Forbid();
            }

            var shape = await _userDisplayManager.BuildEditorAsync(user, updater: _updateModelAccessor.ModelUpdater, isNew: true);

            return View(shape);
        }

        [HttpPost]
        [ActionName(nameof(Create))]
        public async Task<IActionResult> CreatePost()
        {
            var user = new User();

            if (!await _authorizationService.AuthorizeAsync(User, Permissions.ViewUsers, user))
            {
                return Forbid();
            }

            var shape = await _userDisplayManager.UpdateEditorAsync(user, updater: _updateModelAccessor.ModelUpdater, isNew: true);

            if (!ModelState.IsValid)
            {
                return View(shape);
            }

            await _userService.CreateUserAsync(user, null, ModelState.AddModelError);

            if (!ModelState.IsValid)
            {
                return View(shape);
            }

            _notifier.Success(H["User created successfully."]);

            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(string id, string returnUrl)
        {
            // When no id is provided we assume the user is trying to edit their own profile.
            var editingOwnUser = false;
            if (String.IsNullOrEmpty(id))
            {
                id = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!await _authorizationService.AuthorizeAsync(User, Permissions.ManageOwnUserInformation))
                {
                    return Forbid();
                }
                editingOwnUser = true;
            }

            var user = await _userManager.FindByIdAsync(id) as User;
            if (user == null)
            {
                return NotFound();
            }

            if (!editingOwnUser && !await _authorizationService.AuthorizeAsync(User, Permissions.ViewUsers, user))
            {
                return Forbid();
            }

            var shape = await _userDisplayManager.BuildEditorAsync(user, updater: _updateModelAccessor.ModelUpdater, isNew: false);

            ViewData["ReturnUrl"] = returnUrl;

            return View(shape);
        }

        [HttpPost]
        [ActionName(nameof(Edit))]
        public async Task<IActionResult> EditPost(string id, string returnUrl)
        {
            // When no id is provided we assume the user is trying to edit their own profile.
            var editingOwnUser = false;
            if (String.IsNullOrEmpty(id))
            {
                editingOwnUser = true;
                id = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!await _authorizationService.AuthorizeAsync(User, Permissions.ManageOwnUserInformation))
                {
                    return Forbid();
                }
            }

            var user = await _userManager.FindByIdAsync(id) as User;
            if (user == null)
            {
                return NotFound();
            }

            if (!editingOwnUser && !await _authorizationService.AuthorizeAsync(User, Permissions.ViewUsers, user))
            {
                return Forbid();
            }

            var shape = await _userDisplayManager.UpdateEditorAsync(user, updater: _updateModelAccessor.ModelUpdater, isNew: false);

            if (!ModelState.IsValid)
            {
                return View(shape);
            }

            var result = await _userManager.UpdateAsync(user);

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            if (!ModelState.IsValid)
            {
                return View(shape);
            }

            if (String.Equals(User.FindFirstValue(ClaimTypes.NameIdentifier), user.UserId, StringComparison.OrdinalIgnoreCase))
            {
                await _signInManager.RefreshSignInAsync(user);
            }

            _notifier.Success(H["User updated successfully."]);

            if (editingOwnUser)
            {
                if (!String.IsNullOrEmpty(returnUrl))
                {
                    return LocalRedirect(returnUrl);
                }

                return RedirectToAction(nameof(Edit));
            }
            else
            {
                if (!String.IsNullOrEmpty(returnUrl))
                {
                    return LocalRedirect(returnUrl);
                }

                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            var user = await _userManager.FindByIdAsync(id) as User;

            if (user == null)
            {
                return NotFound();
            }

            if (!await _authorizationService.AuthorizeAsync(User, Permissions.ManageUsers, user))
            {
                return Forbid();
            }

            var result = await _userManager.DeleteAsync(user);

            if (result.Succeeded)
            {
                _notifier.Success(H["User deleted successfully."]);
            }
            else
            {
                await _session.CancelAsync();

                _notifier.Error(H["Could not delete the user."]);

                foreach (var error in result.Errors)
                {
                    _notifier.Error(H[error.Description]);
                }
            }

            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> EditPassword(string id)
        {
            var user = await _userManager.FindByIdAsync(id) as User;

            if (user == null)
            {
                return NotFound();
            }

            if (!await _authorizationService.AuthorizeAsync(User, Permissions.ManageUsers, user))
            {
                return Forbid();
            }

            var model = new ResetPasswordViewModel { Email = user.Email };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> EditPassword(ResetPasswordViewModel model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email) as User;

            if (user == null)
            {
                return NotFound();
            }

            if (!await _authorizationService.AuthorizeAsync(User, Permissions.ManageUsers, user))
            {
                return Forbid();
            }

            if (ModelState.IsValid)
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);

                if (await _userService.ResetPasswordAsync(model.Email, token, model.NewPassword, ModelState.AddModelError))
                {
                    _notifier.Success(H["Password updated correctly."]);

                    return RedirectToAction(nameof(Index));
                }
            }

            return View(model);
        }
    }
}
