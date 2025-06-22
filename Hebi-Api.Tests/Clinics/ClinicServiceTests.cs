using FluentAssertions;
using Hebi_Api.Features.Clinics.Dtos;
using Hebi_Api.Features.Clinics.Services;
using Hebi_Api.Features.Core.Common.Enums;
using Hebi_Api.Features.Core.DataAccess;
using Hebi_Api.Features.Core.DataAccess.Models;
using Hebi_Api.Features.Core.DataAccess.UOW;
using Hebi_Api.Tests.UOW;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using System.ComponentModel;

namespace Hebi_Api.Tests.Clinics;

[TestFixture]
public class ClinicServiceTests
{
    private UnitOfWorkFactory _dbFactory;
    private IUnitOfWork _unitOfWorkSqlite;
    private IClinicsService _clinicsService;
    private HebiDbContext _dbContext;

    private UserManager<ApplicationUser> _userManager;

    [SetUp]
    public void Setup()
    {
        _dbFactory = new UnitOfWorkFactory();
        _dbContext = _dbFactory.GetDbContext();
        _unitOfWorkSqlite = _dbFactory.CreateUnitOfWork(true);
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddScoped(typeof(RoleManager<>));
        serviceCollection.AddScoped(_ => _dbContext);

        serviceCollection.AddIdentity<ApplicationUser, IdentityRole<Guid>>()
            .AddUserManager<UserManager<ApplicationUser>>()
            .AddRoleManager<RoleManager<IdentityRole<Guid>>>()
            .AddEntityFrameworkStores<HebiDbContext>()
            .AddDefaultTokenProviders();

        var serviceProvider = serviceCollection.BuildServiceProvider();
        _userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        _dbFactory.AddData(new List<ApplicationUser>() {  new ()
        {
            Id = TestHelper.UserId,
            UserName = "TestPatient",
            FirstName = "Patient",
            LastName = "PatientLastName",
            Email = "test@test.com",
            ClinicId = TestHelper.ClinicId,
            NormalizedUserName = "TESTPATIENT"
        } });

        _clinicsService = new ClinicsService(_unitOfWorkSqlite, TestHelper.CreateHttpContext().Object, _userManager);
    }

    [Test]
    public async Task CreateClinic_ShouldWorks_Properly()
    {
        //Assert
        var dto = new CreateClinicDto()
        {
            Name = "Test",
            Email = "test@test.test",
            PhoneNumber = "1234567890"
        };

        //Act
        var result = await _clinicsService.CreateClinicAsync(dto);

        //Assert
        var clinic = await _unitOfWorkSqlite.ClinicRepository.GetByIdAsync(result);
        clinic.Should().NotBeNull();
        clinic.Name.Should().Be(dto.Name);
        clinic.Email.Should().Be(dto.Email);
        clinic.PhoneNumber.Should().Be(dto.PhoneNumber);
    }

    [Test]
    public async Task CreateClinic_WithUsersShouldWorks_Properly()
    {
        //Assert
        var doctor = new ApplicationUser()
        {
            UserName = "Test",
            Id = Guid.NewGuid(),
            Email = "test@test.com",
            FirstName = "Test",
            LastName = "Test",
            NormalizedUserName = "Test",
        };
        var doctor2 = new ApplicationUser()
        {
            UserName = "Test",
            Id = Guid.NewGuid(),
            FirstName = "Test",
            LastName = "Test",
            Email = "test@test.com",
            NormalizedUserName = "Test",
        };
        var doctor3 = new ApplicationUser()
        {
            UserName = "Test",
            Id = Guid.NewGuid(),
            FirstName = "Test",
            LastName = "Test",
            Email = "test@test.com",
            NormalizedUserName = "Test",
        };
        await _userManager.CreateAsync(doctor);
        await _userManager.CreateAsync(doctor2);
        await _userManager.CreateAsync(doctor3);

        var dto = new CreateClinicDto()
        {
            Name = "Test",
            Email = "test@test.test",
            PhoneNumber = "1234567890",
            DoctorIds = [doctor.Id, doctor2.Id, doctor3.Id],
        };
        //Act
        var result = await _clinicsService.CreateClinicAsync(dto);

        //Assert
        var clinic = await _unitOfWorkSqlite.ClinicRepository.GetByIdAsync(result);
        clinic.Should().NotBeNull();
        clinic.Name.Should().Be(dto.Name);
        clinic.Email.Should().Be(dto.Email);
        clinic.PhoneNumber.Should().Be(dto.PhoneNumber);
        var users = await _unitOfWorkSqlite.UsersRepository.WhereAsync(x => dto.DoctorIds.Contains(x.Id));

        foreach (var user in users)
        {
            user.ClinicId.Should().Be(result);
        }
    }

    [Test]
    public async Task CreateClinic_Should_Add_Creator_If_Not_SuperAdmin()
    {
        // Arrange
        var admin = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "Admin",
            NormalizedUserName = "ADMIN",
            Email = "admin@hebi.com"
        };

        await _userManager.CreateAsync(admin);

        // ��������� IHttpContextAccessor, ����� ������ ID ������
        var mockContextAccessor = TestHelper.CreateHttpContext(admin.Id);

        // ��������� UserManager: IsInRoleAsync ? false (�� �����-�����)
        var mockUserManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null
        );
        mockUserManager.Setup(m => m.IsInRoleAsync(It.IsAny<ApplicationUser>(), UserRoles.SuperAdmin.ToString()))
                       .ReturnsAsync(false);

        // ������ ����� ������� ClinicsService � ������
        var service = new ClinicsService(_unitOfWorkSqlite, mockContextAccessor.Object, mockUserManager.Object);

        var dto = new CreateClinicDto
        {
            Name = "Creator Clinic",
            Email = "creator@clinic.com",
            PhoneNumber = "000111222",
            DoctorIds = new List<Guid>() // �����, ����� ��� ���� ������ ��������
        };

        // Act
        var createdClinicId = await service.CreateClinicAsync(dto);

        // Assert
        var clinic = await _unitOfWorkSqlite.ClinicRepository.GetByIdAsync(createdClinicId);
        clinic.Should().NotBeNull();
        clinic.Name.Should().Be(dto.Name);

        var creator = await _unitOfWorkSqlite.UsersRepository.FirstOrDefaultAsync(x => x.Id == admin.Id);
        creator.ClinicId.Should().Be(createdClinicId);
    }

    [Test]
    public async Task UpdateClinic_WithDoctors_ShouldWorkProper()
    {
        //Assert
        var doctor = new ApplicationUser()
        {
            UserName = "Test",
            Id = Guid.NewGuid(),
            Email = "test@test.com",
            FirstName = "Test",
            LastName = "Test",
            NormalizedUserName = "Test",
        };
        var doctor2 = new ApplicationUser()
        {
            UserName = "Test",
            Id = Guid.NewGuid(),
            FirstName = "Test",
            LastName = "Test",
            Email = "test@test.com",
            NormalizedUserName = "Test",
        };
        var doctor3 = new ApplicationUser()
        {
            UserName = "Test",
            Id = Guid.NewGuid(),
            FirstName = "Test",
            LastName = "Test",
            Email = "test@test.com",
            NormalizedUserName = "Test",
        };
        var admin = new ApplicationUser()
        {
            UserName = "Test",
            Id = Guid.NewGuid(),
            FirstName = "Test",
            LastName = "Test",
            Email = "test@test.com",
            NormalizedUserName = "Test"
        };
        await _userManager.CreateAsync(doctor);
        await _userManager.CreateAsync(doctor2);
        await _userManager.CreateAsync(doctor3);
        await _userManager.CreateAsync(admin);

        var clinicId = Guid.NewGuid();
        _dbFactory.AddData(new List<Clinic>()
        {
            new()
            {
                Id = clinicId,
                Name = "Test",
                PhoneNumber = "0779898989",
                Email = "TestMail",
                Location = "TestLocation"
            }
        });
        var dto = new CreateClinicDto()
        {
            Name = "Test",
            Email = "test@test.test",
            PhoneNumber = "1234567890",
            DoctorIds = [doctor.Id, doctor2.Id, doctor3.Id],
        };
        //Act
        var result = await _clinicsService.UpdateClinicAsync(clinicId, dto);

        //Assert
        var clinic = await _unitOfWorkSqlite.ClinicRepository.GetByIdAsync(clinicId);
        clinic.Should().NotBeNull();
        clinic.Name.Should().Be(dto.Name);
        clinic.Email.Should().Be(dto.Email);
        clinic.PhoneNumber.Should().Be(dto.PhoneNumber);
        var users = await _unitOfWorkSqlite.UsersRepository.WhereAsync(x => dto.DoctorIds.Contains(x.Id));
        foreach (var user in users)
        {
            user.ClinicId.Should().Be(clinicId);
        }
    }
    [Test]
    public async Task DeleteClinic_ShouldRemoveClinic_FromDatabase()
    {
        // Arrange
        var clinic = new Clinic
        {
            Id = Guid.NewGuid(),
            Name = "Test Clinic",
            PhoneNumber = "1234567890",
            Email = "test@clinic.com",
            Location = "Test Location"
        };
        _dbFactory.AddData(new List<Clinic> { clinic });

        // Act
        await _clinicsService.DeleteClinic(clinic.Id);

        // Assert
        var deletedClinic = await _unitOfWorkSqlite.ClinicRepository.GetByIdAsync(clinic.Id);
        deletedClinic.Should().BeNull();
    }

    [Test]
    public async Task GetClinicAsync_ShouldReturnCorrectClinic()
    {
        // Arrange
        var clinic = new Clinic
        {
            Id = Guid.NewGuid(),
            Name = "Test Clinic",
            PhoneNumber = "1234567890",
            Email = "test@clinic.com",
            Location = "Test Location"
        };
        _dbFactory.AddData(new List<Clinic> { clinic });

        // Act
        var result = await _clinicsService.GetClinicAsync(clinic.Id);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(clinic.Id);
        result.Name.Should().Be(clinic.Name);
    }

    [Test]
    public async Task GetClinicAsync_WithInvalidId_ShouldThrowException()
    {
        // Act
        Func<Task> act = async () => await _clinicsService.GetClinicAsync(Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<NullReferenceException>()
            .WithMessage("Clinic");
    }

    [Test]
    public async Task GetListOfClinicsAsync_ShouldReturnPagedClinics()
    {
        // Arrange
        var clinics = new List<Clinic>
        {
            new Clinic { Id = Guid.NewGuid(), Name = "Clinic 1", PhoneNumber = "123", Email = "c1@test.com", Location = "Loc1" },
            new Clinic { Id = Guid.NewGuid(), Name = "Clinic 2", PhoneNumber = "456", Email = "c2@test.com", Location = "Loc2" },
            new Clinic { Id = Guid.NewGuid(), Name = "Clinic 3", PhoneNumber = "789", Email = "c3@test.com", Location = "Loc3" },
        };
        _dbFactory.AddData(clinics);

        var dto = new GetPagedListOfClinicDto
        {
            PageIndex = 0,
            PageSize = 2,
            SortBy = "Name",
            SortDirection = ListSortDirection.Ascending,
        };

        // Act
        var result = await _clinicsService.GetListOfClinicsAsync(dto);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.Select(c => c.Name).Should().BeInAscendingOrder();
    }
}
